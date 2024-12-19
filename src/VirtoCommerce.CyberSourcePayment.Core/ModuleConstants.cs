using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.CyberSourcePayment.Core;

public static class ModuleConstants
{
    public static class Settings
    {
        public static class General
        {
            public static SettingDescriptor CyberSourceSandbox { get; } = new()
            {
                Name = "VirtoCommerce.Payment.CyberSourcePayment.Sandbox",
                GroupName = "Payment|CyberSource",
                ValueType = SettingValueType.Boolean,
                DefaultValue = true,
            };

            public static SettingDescriptor CyberSourceCardTypes { get; } = new()
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
                    yield return CyberSourceSandbox;
                    yield return CyberSourceCardTypes;
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
