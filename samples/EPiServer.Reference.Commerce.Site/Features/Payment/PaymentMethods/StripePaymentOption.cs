using EPiServer.Commerce.Order;
using EPiServer.Framework.Localization;
using EPiServer.Reference.Commerce.Site.Features.Market.Services;
using EPiServer.Reference.Commerce.Site.Features.Payment.Services;
using EPiServer.ServiceLocation;
using Mediachase.Commerce;
using Mediachase.Commerce.Orders;

namespace EPiServer.Reference.Commerce.Site.Features.Payment.PaymentMethods
{
    [ServiceConfiguration(typeof(IPaymentOption))]
    public class StripePaymentOption : PaymentOptionBase
    {
        private readonly IOrderGroupFactory _orderGroupFactory;

        public StripePaymentOption() 
            : this(LocalizationService.Current, ServiceLocator.Current.GetInstance<IOrderGroupFactory>(), ServiceLocator.Current.GetInstance<ICurrentMarket>(), ServiceLocator.Current.GetInstance<LanguageService>(), ServiceLocator.Current.GetInstance<IPaymentService>())
        {
        }

        public StripePaymentOption(LocalizationService localizationService,
            IOrderGroupFactory orderGroupFactory,
            ICurrentMarket currentMarket,
            LanguageService languageService,
            IPaymentService paymentService)
            : base(localizationService, orderGroupFactory, currentMarket, languageService, paymentService)
        {
            _orderGroupFactory = orderGroupFactory;
        }

        public override string SystemKeyword { get; } = "Stripe";

        public string Token { get; set; }

        public string LastFour { get; set; }

        public int Month { get; set; }

        public int Year { get; set; }

        public string Type { get; set; }

        public string CustomerName { get; set; }

        public override IPayment CreatePayment(decimal amount,
            IOrderGroup orderGroup)
        {
            var payment = _orderGroupFactory.CreateCardPayment(orderGroup);
            payment.PaymentMethodId = PaymentMethodId;
            payment.PaymentMethodName = SystemKeyword;
            payment.Amount = amount;
            payment.CreditCardNumber = Token;
            payment.Status = PaymentStatus.Pending.ToString();
            payment.TransactionType = TransactionType.Authorization.ToString();
            payment.ExpirationMonth = Month;
            payment.ExpirationYear = Year;
            payment.CardType = Type;
            payment.CustomerName = CustomerName;
            payment.Properties["stripe_LastFour"] = LastFour;
            return payment;
        }

        public override bool ValidateData()
        {
            return true;
        }
    }
}