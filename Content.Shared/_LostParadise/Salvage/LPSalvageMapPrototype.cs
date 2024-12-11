using Content.Shared._LostParadise.Salvage.Expeditions.Modifiers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._LostParadise.Salvage;

[Prototype]
public sealed partial class LPSalvageMapPrototype : IPrototype
{
    [ViewVariables] [IdDataField] public string ID { get; } = default!;

    /// <summary>
    /// Relative directory path to the given map, i.e. `Maps/Salvage/template.yml`
    /// </summary>
    [DataField(required: true)] public ResPath MapPath;

    /// <summary>
    /// String that describes the size of the map.
    /// </summary>
    [DataField(required: true)]
    public LocId SizeString;
}