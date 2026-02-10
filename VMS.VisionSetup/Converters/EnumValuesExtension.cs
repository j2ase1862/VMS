using System;
using System.Windows.Markup;

namespace VMS.VisionSetup.Converters
{
    public class EnumValuesExtension : MarkupExtension
    {
        private readonly Type _enumType;

        public EnumValuesExtension(Type enumType)
        {
            _enumType = enumType ?? throw new ArgumentNullException(nameof(enumType));
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Enum.GetValues(_enumType);
        }
    }
}
