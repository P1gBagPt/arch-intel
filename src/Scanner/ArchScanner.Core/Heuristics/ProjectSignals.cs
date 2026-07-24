namespace ArchScanner.Core.Heuristics;

/// <summary>
/// Cross-type signals that can't be determined from a symbol's own declaration alone — a class
/// isn't "an EF entity" from anything on the class itself, it's an entity because some DbContext
/// elsewhere in the project exposes a DbSet&lt;T&gt; for it. Computed once per project, before the
/// main Pass 1 walk, via <see cref="ProjectSignalsScanner"/>.
/// </summary>
public sealed class ProjectSignals
{
    public HashSet<string> EfEntityGlobalKeys { get; } = new();

    public HashSet<string> ConfigurationSettingGlobalKeys { get; } = new();

    public Dictionary<string, string> ConfigSectionByGlobalKey { get; } = new();

    /// <summary>Global symbol key (interface) -> lifetime ("Scoped"/"Transient"/"Singleton") for AddXxx&lt;TInterface,TConcrete&gt;() call sites.</summary>
    public Dictionary<string, string> DiRegisteredInterfaceLifetimes { get; } = new();

    /// <summary>Global symbol key (concrete type) -> simple name of the interface it was registered against ("" if self-registered), for Repository/Service confidence scoring.</summary>
    public Dictionary<string, string> DiRegisteredConcreteToInterfaceName { get; } = new();

    /// <summary>Global symbol key (interface) -> global symbol key (concrete) for AddXxx&lt;TInterface,TConcrete&gt;() registrations — the interface -&gt; implementation mapping constructor injection alone can't reveal.</summary>
    public Dictionary<string, string> DiInterfaceToConcreteGlobalKey { get; } = new();

    /// <summary>
    /// Unions N per-project signal sets into one solution-wide set. Needed because the class that
    /// makes a symbol interesting (a DbContext's DbSet&lt;T&gt;, a composition root's AddScoped call)
    /// commonly lives in a different project than the symbol itself (Infrastructure vs. Domain) —
    /// classification must see the whole solution's signals, not just one project's.
    /// </summary>
    public static ProjectSignals Merge(IEnumerable<ProjectSignals> all)
    {
        var merged = new ProjectSignals();

        foreach (var signals in all)
        {
            merged.EfEntityGlobalKeys.UnionWith(signals.EfEntityGlobalKeys);
            merged.ConfigurationSettingGlobalKeys.UnionWith(signals.ConfigurationSettingGlobalKeys);

            foreach (var (key, value) in signals.ConfigSectionByGlobalKey) merged.ConfigSectionByGlobalKey[key] = value;
            foreach (var (key, value) in signals.DiRegisteredInterfaceLifetimes) merged.DiRegisteredInterfaceLifetimes[key] = value;
            foreach (var (key, value) in signals.DiRegisteredConcreteToInterfaceName) merged.DiRegisteredConcreteToInterfaceName[key] = value;
            foreach (var (key, value) in signals.DiInterfaceToConcreteGlobalKey) merged.DiInterfaceToConcreteGlobalKey[key] = value;
        }

        return merged;
    }
}
