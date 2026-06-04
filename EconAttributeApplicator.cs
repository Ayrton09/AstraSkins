using AstraSkins.Models;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace AstraSkins;

internal sealed class EconAttributeApplicator
{
    private const string SignatureKey = "AstraSkins_CAttributeList_SetOrAddAttributeValueByName";
    private readonly ILogger _logger;
    private readonly MemoryFunctionVoid<nint, string, float>? _setOrAddAttributeValueByName;

    public EconAttributeApplicator(ILogger logger)
    {
        _logger = logger;

        try
        {
            var signature = GameData.GetSignature(SignatureKey);
            if (string.IsNullOrWhiteSpace(signature))
            {
                _logger.LogError(
                    "Astra Skins gamedata signature {SignatureKey} is missing. Copy astra_skins.json to addons/counterstrikesharp/gamedata/.",
                    SignatureKey);
                return;
            }

            _setOrAddAttributeValueByName = new MemoryFunctionVoid<nint, string, float>(signature);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Astra Skins failed to load gamedata signature {SignatureKey}. Copy astra_skins.json to addons/counterstrikesharp/gamedata/.",
                SignatureKey);
        }
    }

    public bool ApplyPaintAttributes(CEconItemView item, CosmeticEntry cosmetic, string context)
    {
        if (item.Handle == IntPtr.Zero)
        {
            _logger.LogWarning("Astra Skins econ item invalid while applying {CosmeticId} to {Context}.", cosmetic.Id, context);
            return false;
        }

        if (_setOrAddAttributeValueByName is null)
        {
            _logger.LogWarning(
                "Astra Skins cannot apply dynamic paint attributes for {CosmeticId} on {Context}: gamedata signature is unavailable.",
                cosmetic.Id,
                context);
            return false;
        }

        try
        {
            item.AttributeList.Attributes.RemoveAll();
            item.NetworkedDynamicAttributes.Attributes.RemoveAll();

            SetPaintAttributes(item.AttributeList.Handle, cosmetic);
            SetPaintAttributes(item.NetworkedDynamicAttributes.Handle, cosmetic);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins attribute update failed for {CosmeticId} on {Context}.", cosmetic.Id, context);
            return false;
        }
    }

    public void ClearPaintAttributes(CEconItemView item, string context)
    {
        if (item.Handle == IntPtr.Zero)
        {
            _logger.LogWarning("Astra Skins econ item invalid while clearing paint attributes on {Context}.", context);
            return;
        }

        try
        {
            item.AttributeList.Attributes.RemoveAll();
            item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to clear paint attributes on {Context}.", context);
        }
    }

    private void SetPaintAttributes(nint attributeListHandle, CosmeticEntry cosmetic)
    {
        _setOrAddAttributeValueByName!.Invoke(attributeListHandle, "set item texture prefab", cosmetic.PaintKit);
        _setOrAddAttributeValueByName.Invoke(attributeListHandle, "set item texture seed", cosmetic.Seed);
        _setOrAddAttributeValueByName.Invoke(attributeListHandle, "set item texture wear", cosmetic.Wear);
    }
}
