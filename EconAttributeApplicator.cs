using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace AstraSkins;

// One sticker on one of the weapon's five slots (0 to 4).
internal readonly record struct StickerAttachment(int Slot, int StickerId);

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

    public bool ApplyPaintAttributes(
        CEconItemView item,
        string cosmeticId,
        int paintKit,
        int seed,
        float wear,
        string context,
        int? statTrak = null,
        IReadOnlyList<StickerAttachment>? stickers = null,
        int? keychainId = null)
    {
        if (item.Handle == IntPtr.Zero)
        {
            _logger.LogWarning("Astra Skins econ item invalid while applying {CosmeticId} to {Context}.", cosmeticId, context);
            return false;
        }

        if (_setOrAddAttributeValueByName is null)
        {
            _logger.LogWarning(
                "Astra Skins cannot apply dynamic paint attributes for {CosmeticId} on {Context}: gamedata signature is unavailable.",
                cosmeticId,
                context);
            return false;
        }

        try
        {
            item.AttributeList.Attributes.RemoveAll();
            item.NetworkedDynamicAttributes.Attributes.RemoveAll();

            SetPaintAttributes(item.AttributeList.Handle, paintKit, seed, wear);
            SetPaintAttributes(item.NetworkedDynamicAttributes.Handle, paintKit, seed, wear);
            SetAttachmentAttributes(item.AttributeList.Handle, stickers, keychainId);
            SetAttachmentAttributes(item.NetworkedDynamicAttributes.Handle, stickers, keychainId);
            SetStatTrakAttributes(item, statTrak);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins attribute update failed for {CosmeticId} on {Context}.", cosmeticId, context);
            return false;
        }
    }

    public void UpdateStatTrak(CEconItemView item, int statTrak)
    {
        if (item.Handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SetStatTrakAttributes(item, statTrak);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Astra Skins failed to update the StatTrak attribute.");
        }
    }

    public void ClearPaintAttributes(CEconItemView item, string context, int? statTrak = null)
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
            SetStatTrakAttributes(item, statTrak);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to clear paint attributes on {Context}.", context);
        }
    }

    // Removing every attribute also removes the kill eater pair that feeds the
    // digits on the StatTrak module, which is why a seed or wear change used to
    // leave the counter blank. Re-add it whenever the list is rebuilt.
    private void SetStatTrakAttributes(CEconItemView item, int? statTrak)
    {
        if (statTrak is null || _setOrAddAttributeValueByName is null)
        {
            return;
        }

        // "kill eater" is stored as an unsigned int, but the setter takes a float
        // and writes it verbatim into the union. Passing 20f would be read back as
        // its bit pattern (1101004800), so reinterpret the int bits instead.
        var count = BitConverter.Int32BitsToSingle(statTrak.Value);
        var scoreType = BitConverter.Int32BitsToSingle(0);

        foreach (var handle in new[] { item.AttributeList.Handle, item.NetworkedDynamicAttributes.Handle })
        {
            _setOrAddAttributeValueByName.Invoke(handle, "kill eater", count);
            _setOrAddAttributeValueByName.Invoke(handle, "kill eater score type", scoreType);
        }
    }

    // Sticker and charm ids are integer attributes like the kill counter, so
    // the int bits go through the float parameter unchanged. Only the ids are
    // written: wear, scale, rotation and offsets left absent render the
    // sticker at its stock size and position, the way a freshly applied
    // sticker looks in the game.
    private void SetAttachmentAttributes(nint attributeListHandle, IReadOnlyList<StickerAttachment>? stickers, int? keychainId)
    {
        if (stickers is not null)
        {
            foreach (var sticker in stickers)
            {
                if (sticker.Slot < 0 || sticker.StickerId <= 0)
                {
                    continue;
                }

                _setOrAddAttributeValueByName!.Invoke(attributeListHandle, $"sticker slot {sticker.Slot} id", BitConverter.Int32BitsToSingle(sticker.StickerId));
            }
        }

        if (keychainId is > 0)
        {
            _setOrAddAttributeValueByName!.Invoke(attributeListHandle, "keychain slot 0 id", BitConverter.Int32BitsToSingle(keychainId.Value));
        }
    }

    private void SetPaintAttributes(nint attributeListHandle, int paintKit, int seed, float wear)
    {
        _setOrAddAttributeValueByName!.Invoke(attributeListHandle, "set item texture prefab", paintKit);
        _setOrAddAttributeValueByName.Invoke(attributeListHandle, "set item texture seed", seed);
        _setOrAddAttributeValueByName.Invoke(attributeListHandle, "set item texture wear", wear);
    }
}
