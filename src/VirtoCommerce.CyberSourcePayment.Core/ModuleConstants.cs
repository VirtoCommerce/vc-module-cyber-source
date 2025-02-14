using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.CyberSourcePayment.Core;

public static class ModuleConstants
{
    public static class Settings
    {
        public static readonly string SingleMessageMode = "Single Message";
        public static readonly string DualMessageMode = "Dual Message";

        public static class General
        {
            public static SettingDescriptor Sandbox { get; } = new()
            {
                Name = "VirtoCommerce.Payment.CyberSourcePayment.Sandbox",
                GroupName = "Payment|CyberSource",
                ValueType = SettingValueType.Boolean,
                DefaultValue = true,
            };

            public static SettingDescriptor PaymentMode { get; } = new()
            {
                Name = "VirtoCommerce.Payment.CyberSourcePayment.PaymentMode",
                GroupName = "Payment|CyberSource",
                ValueType = SettingValueType.ShortText,
                DefaultValue = SingleMessageMode,
                AllowedValues = [SingleMessageMode, DualMessageMode],
            };

            public static SettingDescriptor CardTypes { get; } = new()
            {
                Name = "VirtoCommerce.Payment.CyberSourcePayment.CardTypes",
                GroupName = "Payment|CyberSource",
                ValueType = SettingValueType.ShortText,
                DefaultValue = "VISA, MASTERCARD, AMEX, DISCOVER, DINERSCLUB, JCB, CARTESBANCAIRES, MAESTRO, CUP",
            };

            public static IEnumerable<SettingDescriptor> AllGeneralSettings
            {
                get
                {
                    yield return Sandbox;
                    yield return PaymentMode;
                    yield return CardTypes;
                }
            }
        }

        public static IEnumerable<SettingDescriptor> AllSettings
        {
            get
            {
                return General.AllGeneralSettings;
            }
        }
    }
}
