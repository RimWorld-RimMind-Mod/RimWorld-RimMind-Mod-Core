using System.Collections.Generic;

namespace RimMind.Presentation.UI.Settings
{
    public sealed class SettingsFormModel
    {
        public SettingsFormModel(IReadOnlyList<SettingsFormSection> sections)
        {
            Sections = sections;
        }

        public IReadOnlyList<SettingsFormSection> Sections { get; }
    }

    public sealed class SettingsFormSection
    {
        public SettingsFormSection(string titleKey, IReadOnlyList<SettingsFormRow> rows)
        {
            TitleKey = titleKey;
            Rows = rows;
        }

        public string TitleKey { get; }
        public IReadOnlyList<SettingsFormRow> Rows { get; }
    }

    public sealed class SettingsFormRow
    {
        public SettingsFormRow(string labelKey, string descriptionKey, string controlId)
        {
            LabelKey = labelKey;
            DescriptionKey = descriptionKey;
            ControlId = controlId;
        }

        public string LabelKey { get; }
        public string DescriptionKey { get; }
        public string ControlId { get; }
    }
}
