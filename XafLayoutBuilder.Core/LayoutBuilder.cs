using System.Linq.Expressions;

namespace XafLayoutBuilder.Core;

/// <summary>Fluent DetailView layout for <typeparamref name="T"/>. See skills/xaf-layout-builder/SKILL.md for the full surface.</summary>
public sealed class LayoutBuilder<T> {
    readonly List<LayoutNodeSpec> nodes = [];
    readonly HashSet<string> hidden = [];
    UnplacedMembers unplaced = UnplacedMembers.Fail;

    LayoutBuilder() { }

    public static LayoutBuilder<T> Create() => new();

    /// <summary>
    /// HIER-001: starts from <typeparamref name="TBase"/>'s DetailView layout, so a derived class adds to its base's form
    /// instead of repeating it. The base layout is read when this is called, so the derived form follows later changes to
    /// the base. Every member the derived class adds must still be placed or hidden (XLB002).
    /// </summary>
    public static LayoutBuilder<T> Extend<TBase>() where TBase : ISupportViewLayoutCustomization =>
        Extend(TBase.BuildDetailViewLayout()
            ?? throw new LayoutSpecException($"{typeof(T).Name}: {typeof(TBase).Name} has no DetailView layout to extend."));

    /// <summary>HIER-001: starts from <paramref name="baseLayout"/>, the layout of a class <typeparamref name="T"/> derives from (a registered one, say).</summary>
    public static LayoutBuilder<T> Extend(DetailLayoutSpec baseLayout) {
        Hierarchy.EnsureDerives(typeof(T), baseLayout.TypeName);
        var builder = new LayoutBuilder<T>();
        builder.nodes.AddRange(baseLayout.Nodes);
        builder.hidden.UnionWith(baseLayout.HiddenMembers);
        if (baseLayout.UnplacedGroupId is { } catchAll) builder.unplaced = UnplacedMembers.AppendToGroup(catchAll);
        return builder;
    }

    /// <summary>
    /// HIER-001: adds items and groups to a group the layout already has, found by id anywhere in the tree, tabs included:
    /// how a derived class puts its own members into its base's groups. The group's caption and options stay as the layout
    /// it extends set them.
    /// </summary>
    public LayoutBuilder<T> InGroup(string id, Action<GroupBuilder<T>> configure) {
        var additions = new GroupBuilder<T>(id);
        configure(additions);
        var added = additions.Build();
        if (added.Caption is not null || added.Collapsible || added.Direction != FlowDirection.Vertical || added.RelativeSize is not null || added.ImageName is not null)
            throw new LayoutSpecException($"{typeof(T).Name}: InGroup adds items and groups to \"{id}\"; its caption and options come from the layout it extends.");
        var found = false;
        for (var i = 0; i < nodes.Count; i++) nodes[i] = Add(nodes[i]);
        if (!found) throw new LayoutSpecException($"{typeof(T).Name}: InGroup(\"{id}\") names no group in the layout.");
        return this;

        LayoutNodeSpec Add(LayoutNodeSpec node) {
            switch (node) {
                case LayoutGroupSpec g when g.Id == id:
                    found = true;
                    return g with { Children = [.. g.Children, .. added.Children] };
                case LayoutGroupSpec g:
                    return g with { Children = g.Children.Select(Add).ToArray() };
                case TabbedGroupSpec t:
                    return t with { Tabs = t.Tabs.Select(tab => (LayoutGroupSpec)Add(tab)).ToArray() };
                default:
                    return node;
            }
        }
    }

    public LayoutBuilder<T> Group(string id, Action<GroupBuilder<T>> configure) {
        var g = new GroupBuilder<T>(id);
        configure(g);
        nodes.Add(g.Build());
        return this;
    }

    public LayoutBuilder<T> Tabs(string id, Action<TabsBuilder<T>> configure) {
        var t = new TabsBuilder<T>(id);
        configure(t);
        nodes.Add(t.Build());
        return this;
    }

    /// <summary>An item directly under the root group. Exists so an exported layout whose user dragged an editor to the root round-trips.</summary>
    public LayoutBuilder<T> Item(Expression<Func<T, object?>> member, double? relativeSize = null) {
        nodes.Add(new LayoutItemSpec(MemberPath.Of(member), relativeSize));
        return this;
    }

    /// <summary>Do not place this member at all. Differs from ListView hiding: a hidden detail item is gone, not "available".</summary>
    public LayoutBuilder<T> Hide(Expression<Func<T, object?>> member) {
        hidden.Add(MemberPath.Of(member));
        return this;
    }

    /// <summary>
    /// What happens to a visible member this layout neither places nor hides. The default is
    /// <see cref="UnplacedMembers.Fail"/>: startup stops with XLB002 naming the member, so a property added to the
    /// class cannot quietly vanish from the form. <see cref="UnplacedMembers.AppendToGroup"/> relaxes that for this
    /// class by collecting whatever is left in one group at the end of the form.
    /// </summary>
    public LayoutBuilder<T> Unplaced(UnplacedMembers policy) {
        unplaced = policy ?? UnplacedMembers.Fail;
        return this;
    }

    /// <summary>Freezes and validates with <see cref="LayoutSpecChecks.Validate(DetailLayoutSpec)"/>, which lists the rules.</summary>
    public DetailLayoutSpec Build() {
        var spec = new DetailLayoutSpec(typeof(T).FullName!, nodes.ToArray(), hidden.ToArray(), unplaced.GroupId);
        LayoutSpecChecks.Validate(spec);
        return spec;
    }
}

public sealed class GroupBuilder<T> {
    readonly string id;
    readonly List<LayoutNodeSpec> children = [];
    string? caption;
    FlowDirection direction = FlowDirection.Vertical;
    bool collapsible;
    double? relativeSize;
    string? imageName;

    internal GroupBuilder(string id) { this.id = id; }

    public GroupBuilder<T> Caption(string caption) { this.caption = caption; return this; }
    public GroupBuilder<T> Flow(FlowDirection direction) { this.direction = direction; return this; }
    public GroupBuilder<T> Collapsible() { collapsible = true; return this; }
    public GroupBuilder<T> RelativeSize(double size) { relativeSize = size; return this; }
    public GroupBuilder<T> Image(string imageName) { this.imageName = imageName; return this; }

    public GroupBuilder<T> Item(Expression<Func<T, object?>> member, double? relativeSize = null) {
        children.Add(new LayoutItemSpec(MemberPath.Of(member), relativeSize));
        return this;
    }

    public GroupBuilder<T> Group(string id, Action<GroupBuilder<T>> configure) {
        var g = new GroupBuilder<T>(id);
        configure(g);
        children.Add(g.Build());
        return this;
    }

    public GroupBuilder<T> Tabs(string id, Action<TabsBuilder<T>> configure) {
        var t = new TabsBuilder<T>(id);
        configure(t);
        children.Add(t.Build());
        return this;
    }

    internal LayoutGroupSpec Build() => new(id, children.ToArray(), caption, direction, collapsible, relativeSize, imageName);
}

public sealed class TabsBuilder<T> {
    readonly string id;
    readonly List<LayoutGroupSpec> tabs = [];

    internal TabsBuilder(string id) { this.id = id; }

    /// <summary>One tab holding a single member (typically a collection). Tab id = member name.</summary>
    public TabsBuilder<T> TabFor(Expression<Func<T, object?>> member, string? imageName = null, string? caption = null) {
        var name = MemberPath.Of(member);
        tabs.Add(new LayoutGroupSpec(name, [new LayoutItemSpec(name)], caption, ImageName: imageName));
        return this;
    }

    /// <summary>A tab with arbitrary content.</summary>
    public TabsBuilder<T> Tab(string id, Action<GroupBuilder<T>> configure) {
        var g = new GroupBuilder<T>(id);
        configure(g);
        tabs.Add(g.Build());
        return this;
    }

    internal TabbedGroupSpec Build() => new(id, tabs.ToArray());
}

internal static class Hierarchy {
    /// <summary>HIER-001: <paramref name="derived"/> must derive from the class a base spec was built for.</summary>
    public static void EnsureDerives(Type derived, string baseTypeName) {
        for (var t = derived.BaseType; t is not null; t = t.BaseType)
            if (t.FullName == baseTypeName) return;
        throw new LayoutSpecException($"{derived.Name} does not derive from {baseTypeName}, so it cannot extend its layout.");
    }
}
