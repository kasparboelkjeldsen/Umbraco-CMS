namespace Umbraco.Cms.Core.Models;

/// <summary>
///     Used when determining available compositions for a given content type
/// </summary>
public class ContentTypeAvailableCompositionsResult
{
    public ContentTypeAvailableCompositionsResult(IContentTypeComposition composition, bool allowed, string[] allowedContingentOnSwitchFromCompositionAliases, bool switchable)
    {
        Composition = composition;
        Allowed = allowed;
        SwitchableFrom = allowedContingentOnSwitchFromCompositionAliases;
        Switchable = switchable;
    }

    public IContentTypeComposition Composition { get; }

    public string[] SwitchableFrom { get; set; }
    public bool Allowed { get; }
    public bool Switchable { get; }
}
