namespace VpnBypass.Service;

// A projekt rögzített WFP-GUID-jai (a specből). Az éles IDriverClient ezekkel hozza létre a
// providert + 2 sublayert, és adja át a GUID-okat az Initialize-nak. A dry-run nem használja terelésre.
public static class DriverConstants
{
    public static readonly Guid Provider = Guid.Parse("25289158-e7eb-4de2-a4c1-ac1babf1fbf5");
    public static readonly Guid BaselineSublayer = Guid.Parse("6e0273b7-33fd-4cfc-9312-8cffcc27dbdc");
    public static readonly Guid DnsSublayer = Guid.Parse("63359fcc-b89d-4c49-81c9-c011a3eee3cd");
}
