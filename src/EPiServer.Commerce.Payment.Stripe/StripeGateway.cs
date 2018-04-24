using EPiServer.Commerce.Order;
using EPiServer.Framework.Localization;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using Mediachase.Commerce;
using Mediachase.Commerce.Customers;
using Mediachase.Commerce.Orders;
using Mediachase.Commerce.Plugins.Payment;
using Mediachase.Commerce.Security;
using Stripe;
using System;

namespace EPiServer.Commerce.Payment.Stripe
{
    public class StripeGateway : AbstractPaymentGateway, IPaymentPlugin
    {
        private decimal _orderTaxTotal;
        private readonly Injected<ITaxCalculator> _taxCalculator = default(Injected<ITaxCalculator>);
        private readonly Injected<IOrderRepository> _orderRepository = default(Injected<IOrderRepository>);
        private readonly Injected<LocalizationService> _localizationService = default(Injected<LocalizationService>);
        private readonly StripeCustomerService _stripeCustomerService = new StripeCustomerService();
        private readonly StripeChargeService _stripeChargeService = new StripeChargeService();
        private readonly StripeRefundService _stripeRefundService = new StripeRefundService();

        public override bool ProcessPayment(Mediachase.Commerce.Orders.Payment payment,
            ref string message)
        {
            var orderGroup = payment.Parent.Parent;
            var paymentProcessingResult = ProcessPayment(orderGroup, payment);
            message += paymentProcessingResult.Message;
            return paymentProcessingResult.IsSuccessful;
        }
        
        public PaymentProcessingResult ProcessPayment(IOrderGroup orderGroup, IPayment payment)
        {
            if (!(payment is ICreditCardPayment creditCardPayment))
            {
                return PaymentProcessingResult.CreateUnsuccessfulResult(Translate("PaymentNotSpecified"));
            }

            var billingAddress = creditCardPayment.BillingAddress;
            if (billingAddress == null)
            {
                return PaymentProcessingResult.CreateUnsuccessfulResult(Translate("PaymentNotSpecified"));
            }

            if (string.IsNullOrEmpty(billingAddress.Email))
            {
                return PaymentProcessingResult.CreateUnsuccessfulResult(Translate("PaymentNotSpecified"));
            }

            if (_orderTaxTotal == 0)
            {
                _orderTaxTotal = _taxCalculator.Service.GetTaxTotal(orderGroup, orderGroup.Market, orderGroup.Currency).Amount;
            }

            if (orderGroup is ICart cart)
            {
                return Charge(cart, creditCardPayment, billingAddress);
            }
            // the order which is created by Commerce Manager
            if (!(orderGroup is IPurchaseOrder))
            {
                return PaymentProcessingResult.CreateUnsuccessfulResult(Translate("UnsupportedPaymentType") + $".  {orderGroup.GetType().AssemblyQualifiedName}");
            }

            if (payment.TransactionType == TransactionType.Capture.ToString())
            {
                return ProcessPaymentCapture(orderGroup, creditCardPayment, billingAddress);
            }

            // When "Refund" shipment in Commerce Manager, this method will be invoked with the TransactionType is Credit
            return payment.TransactionType == TransactionType.Credit.ToString() ? 
                ProcessPaymentRefund(orderGroup, creditCardPayment) : 
                PaymentProcessingResult.CreateUnsuccessfulResult(Translate("UnsupportedPaymentType") + $".  {orderGroup.GetType().AssemblyQualifiedName}");
        }

        private PaymentProcessingResult Charge(IOrderGroup cart,
            ICreditCardPayment creditCardPayment,
            IOrderAddress billingAddress)
        {
            var contact = CustomerContext.Current.GetContactById(cart.CustomerId);
            StripeCustomer stripeCustomer = null;
            try
            {
                if (contact != null)
                {
                    if (!string.IsNullOrEmpty(contact?["StripeId"]?.ToString()))
                    {
                        stripeCustomer = _stripeCustomerService.Get(contact["StripeId"].ToString());
                    }

                    if (stripeCustomer == null)
                    {
                        stripeCustomer = _stripeCustomerService.Create(new StripeCustomerCreateOptions
                        {
                            Email = contact.Email,
                            SourceToken = creditCardPayment.CreditCardNumber
                        });

                        contact["StripeId"] = stripeCustomer.Id;
                        contact.SaveChanges();
                    }
                }

                Enum.TryParse(creditCardPayment.TransactionType, out TransactionType transactionType);
                var options = new StripeChargeCreateOptions
                {
                    Amount = (int) creditCardPayment.Amount * GetMultiplier(cart.Currency),
                    Description = "Ecommerce Charge",
                    Currency = cart.Currency.ToString().ToLower(),
                    Capture = transactionType == TransactionType.Capture ||
                              transactionType == TransactionType.CaptureOnly
                };

                if (stripeCustomer != null)
                {
                    options.CustomerId = stripeCustomer.Id;

                }
                else
                {
                    options.SourceTokenOrExistingSourceId = creditCardPayment.CreditCardNumber;
                }

                var charge = _stripeChargeService.Create(options);

                if (!string.IsNullOrEmpty(charge.FailureCode))
                {
                    return PaymentProcessingResult.CreateUnsuccessfulResult(Translate(charge.Outcome.Reason));
                }
                creditCardPayment.ProviderPaymentId = charge.Id;
                creditCardPayment.ProviderTransactionID = charge.BalanceTransactionId;
                creditCardPayment.Properties["stripe_CustomerId"] = stripeCustomer?.Id;
                return PaymentProcessingResult.CreateSuccessfulResult("");
            }
            catch (StripeException e)
            {
                switch (e.StripeError.ErrorType)
                {
                    case "card_error":
                        return PaymentProcessingResult.CreateUnsuccessfulResult(Translate(e.StripeError.Code));
                    default:
                        return PaymentProcessingResult.CreateUnsuccessfulResult(e.StripeError.Message);
                }
            }
        }

        private PaymentProcessingResult ProcessPaymentRefund(IOrderGroup orderGroup, ICreditCardPayment creditCardPayment)
        {
            var refundAmount = creditCardPayment.Amount;
            var purchaseOrder = (IPurchaseOrder) orderGroup;
            if (purchaseOrder == null || refundAmount <= 0 || string.IsNullOrEmpty(creditCardPayment.ProviderPaymentId))
            {
                return PaymentProcessingResult.CreateUnsuccessfulResult(Translate("RefundError"));
            }

            try
            {
                var refundOptions = new StripeRefundCreateOptions()
                {
                    Amount = (int)refundAmount * GetMultiplier(orderGroup.Currency),
                    Reason = StripeRefundReasons.RequestedByCustomer
                };
                
                var refund = _stripeRefundService.Create(creditCardPayment.ProviderPaymentId, refundOptions);
                // Extract the response details.
                creditCardPayment.TransactionID = refund.Id;

                var message = $"[{creditCardPayment.PaymentMethodName}] [RefundTransaction-{refund.Id}] " +
                              $"Response: {refund.Status} at Timestamp={refund.Created.ToString()}: {refund.Amount}{refund.Currency}";

                // add a new order note about this refund
                AddNoteToPurchaseOrder("REFUND", message, purchaseOrder.CustomerId, purchaseOrder);

                _orderRepository.Service.Save(purchaseOrder);

                return PaymentProcessingResult.CreateSuccessfulResult(message);
            }
            catch (StripeException e)
            {
                switch (e.StripeError.ErrorType)
                {
                    case "card_error":
                        return PaymentProcessingResult.CreateUnsuccessfulResult(Translate(e.StripeError.Code));
                    default:
                        return PaymentProcessingResult.CreateUnsuccessfulResult(e.StripeError.Message);
                }
            }
            
            
        }

        private PaymentProcessingResult ProcessPaymentCapture(IOrderGroup orderGroup, 
            ICreditCardPayment creditCardPayment,
            IOrderAddress billingAddress)
        {
            if (string.IsNullOrEmpty(creditCardPayment.ProviderPaymentId))
            {
                return Charge(orderGroup, creditCardPayment, billingAddress);
            }
            try
            {
                var capture = _stripeChargeService.Capture(creditCardPayment.ProviderPaymentId, new StripeChargeCaptureOptions());
                if (!string.IsNullOrEmpty(capture.FailureCode))
                {
                    return PaymentProcessingResult.CreateUnsuccessfulResult(Translate(capture.Outcome.Reason));
                }
                creditCardPayment.ProviderPaymentId = capture.Id;
                creditCardPayment.ProviderTransactionID = capture.BalanceTransactionId;
                return PaymentProcessingResult.CreateSuccessfulResult("");
            }
            catch (StripeException e)
            {
                switch (e.StripeError.ErrorType)
                {
                    case "card_error":
                        return PaymentProcessingResult.CreateUnsuccessfulResult(Translate(e.StripeError.Code));
                    default:
                        return PaymentProcessingResult.CreateUnsuccessfulResult(e.StripeError.Message);
                }
            }
            
        }

        private int GetMultiplier(Currency currency)
        {
            switch (currency.Format.CurrencyDecimalDigits)
            {
                case 0:
                    return 1;
                case 1:
                    return 10;
                case 2:
                    return 100;
                case 3:
                    return 1000;
                case 4:
                    return 10000;
                default:
                    return 1;
            }
        }

        private string Translate(string languageKey)
        {
            return _localizationService.Service.GetString($"/Commerce/Stripe/{languageKey}");
        }

        private void AddNoteToPurchaseOrder(string title, string detail, Guid customerId, IPurchaseOrder purchaseOrder)
        {
            var orderNote = purchaseOrder.CreateOrderNote();
            orderNote.Type = OrderNoteTypes.System.ToString();
            orderNote.CustomerId = customerId != Guid.Empty ? customerId : PrincipalInfo.CurrentPrincipal.GetContactId();
            orderNote.Title = !string.IsNullOrEmpty(title) ? title : detail.Substring(0, Math.Min(detail.Length, 24)) + "...";
            orderNote.Detail = detail;
            orderNote.Created = DateTime.UtcNow;
            purchaseOrder.Notes.Add(orderNote);
        }
    }
}
