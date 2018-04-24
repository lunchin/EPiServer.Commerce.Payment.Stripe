using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;
using Mediachase.BusinessFoundation.Configuration;
using Mediachase.BusinessFoundation.Data;
using Mediachase.BusinessFoundation.Data.Meta.Management;
using Mediachase.Commerce;
using Mediachase.Commerce.Customers;
using Mediachase.Commerce.Markets;
using Mediachase.Commerce.Orders;
using Mediachase.Commerce.Orders.Dto;
using Mediachase.Commerce.Orders.Managers;
using Mediachase.MetaDataPlus;
using Mediachase.MetaDataPlus.Configurator;
using Stripe;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace EPiServer.Commerce.Payment.Stripe
{
    [InitializableModule]
    [ModuleDependency(typeof(Initialization.InitializationModule))]
    public class InitializationModule : IConfigurableModule
    {
        public void ConfigureContainer(ServiceConfigurationContext context)
        {
           
        }

        public void Initialize(InitializationEngine context)
        {
            AddCustomerStripeFieldIfNeccessary();
            AddMetaFieldsIfNesccessary();
            StripeConfiguration.SetApiKey(ConfigurationManager.AppSettings["stripe:SecretKey"]);
            ConfigurePaymentMethod(context.Locate.Advanced.GetInstance<IMarketService>());
        }

        public void Uninitialize(InitializationEngine context)
        {
            //Add uninitialization logic
        }

        private void AddMetaFieldsIfNesccessary()
        {
            var orderContext = OrderContext.MetaDataContext;
            if (orderContext == null)
            {
                return;
            }
            
            var creditCardClass = Mediachase.MetaDataPlus.Configurator.MetaClass.Load(orderContext, "CreditCardPayment");
            if (creditCardClass == null)
            {
                return;
            }

            TryAddStringMetaField(orderContext, creditCardClass, "stripe_CustomerId");
            TryAddStringMetaField(orderContext, creditCardClass, "stripe_LastFour");
        }

        private void AddCustomerStripeFieldIfNeccessary()
        {
            var bafConnectionString = BusinessFoundationConfigurationSection.Instance.Connection.Database;
            if (bafConnectionString == null)
            {
                return;
            }

            DataContext.Current = new DataContext(bafConnectionString);
            Mediachase.BusinessFoundation.Data.Modules.ModuleManager.InitializeActiveModules();
            var fields = DataContext.Current.MetaModel.MetaClasses[ContactEntity.ClassName].Fields;
            if (fields.Contains("StripeId"))
            {
                return;
            }

            var manager = DataContext.Current.MetaModel;
            var mc = manager.MetaClasses[ContactEntity.ClassName];

            using (var builder = new MetaFieldBuilder(mc))
            {
                builder.CreateText("StripeId", "StripeId", false, 512, false);
                builder.SaveChanges();
            }
        }

        private void ConfigurePaymentMethod(IMarketService marketService)
        {
            var allMarkets = marketService.GetAllMarkets().Where(x => x.IsEnabled).ToList();
            foreach (var language in allMarkets.SelectMany(x => x.Languages).Distinct())
            {
                var paymentMethodDto = PaymentManager.GetPaymentMethodBySystemName("Stripe", language.TwoLetterISOLanguageName, true);
                if (paymentMethodDto != null && paymentMethodDto.PaymentMethod.Any())
                {
                    continue;
                }

                AddPaymentMethod(Guid.NewGuid(),
                    "Stripe Credit card",
                    "Stripe",
                    "Stripe Credit card payment",
                    "Mediachase.Commerce.Orders.CreditCardPayment, Mediachase.Commerce",
                    "EPiServer.Commerce.Payment.Stripe.StripeGateway,EPiServer.Commerce.Payment.Stripe",
                    true,
                    1,
                    allMarkets,
                    language,
                    paymentMethodDto);


            }
        }

        private static void AddPaymentMethod(Guid id, string name, string systemKeyword, string description, string implementationClass, string gatewayClass,
            bool isDefault, int orderIndex, IEnumerable<IMarket> markets, CultureInfo language, PaymentMethodDto paymentMethodDto)
        {
            var row = paymentMethodDto.PaymentMethod.AddPaymentMethodRow(id, name, description, language.TwoLetterISOLanguageName,
                systemKeyword, true, isDefault, gatewayClass,
                implementationClass, false, orderIndex, DateTime.Now, DateTime.Now);

            var paymentMethod = new PaymentMethod(row);
            paymentMethod.MarketId.AddRange(markets.Where(x => x.IsEnabled && x.Languages.Contains(language)).Select(x => x.MarketId).Distinct());
            paymentMethod.SaveChanges();
        }

        private void TryAddStringMetaField(MetaDataContext context, Mediachase.MetaDataPlus.Configurator.MetaClass metaClass, string name)
        {
            var metaField = Mediachase.MetaDataPlus.Configurator.MetaField.Load(context, name) ?? Mediachase.MetaDataPlus.Configurator.MetaField.Create(
                                context: context,
                                metaNamespace: metaClass.Namespace,
                                name: name,
                                friendlyName: name,
                                description: name,
                                dataType: MetaDataType.NVarChar,
                                length: 4000,
                                allowNulls: true,
                                multiLanguageValue: false,
                                allowSearch: false,
                                isEncrypted: false);

            if (metaClass.MetaFields.All(x => x.Id != metaField.Id))
            {
                metaClass.AddField(metaField);
            }
        }
    }
}