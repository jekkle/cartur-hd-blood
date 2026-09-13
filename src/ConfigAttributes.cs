using System;

namespace CarturHDBlood
{
    /// ConfigurationManager reads these by duck typing - it looks for fields with these exact
    /// names on whatever object is passed as a config tag, so the manager never has to be
    /// referenced, and none of this has any effect when it is not installed.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public bool? IsAdvanced;
        public int? Order;
    }
}
