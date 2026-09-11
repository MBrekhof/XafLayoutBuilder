using System.Linq.Expressions;

namespace XafLayoutBuilder.Core;

/// <summary>Fluent DetailView layout for <typeparamref name="T"/>. See skills/xaf-layout-builder/SKILL.md for the full surface.</summary>
public sealed class LayoutBuilder<T> {
    readonly List<LayoutNodeSpec> nodes = [];
    readonly HashSet<string> hidden = [];

    LayoutBuilder() { }

    public static LayoutBuilder<T> Create() => new();

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

    /// <summary>Do not place this member at all. Differs from ListView hiding: a hidden detail item is gone, not "available".</summary>
    public LayoutBuilder<T> Hide(Expression<Func<T, object?>> member) {
        hidden.Add(MemberPath.Of(member));
        return this;
    }

    /// <summary>Validates and freezes. Throws <see cref="LayoutSpecException"/> on duplicate items, hidden-and-placed items, duplicate group ids.</summary>
    public DetailLayoutSpec Build() {
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in nodes) Walk(n);

        return new DetailLayoutSpec(typeof(T).FullName!, nodes.ToArray(), hidden.ToArray());

        void Walk(LayoutNodeSpec node) {
            switch (node) {
                case LayoutItemSpec item:
                    if (!members.Add(item.Member)) throw new LayoutSpecException($"{typeof(T).Name}: member '{item.Member}' is placed twice.");
                    if (hidden.Contains(item.Member)) throw new LayoutSpecException($"{typeof(T).Name}: member '{item.Member}' is both placed and hidden.");
                    break;
                case LayoutGroupSpec g:
                    AddGroupId(g.Id);
                    foreach (var c in g.Children) Walk(c);
                    break;
                case TabbedGroupSpec t:
                    AddGroupId(t.Id);
                    foreach (var tab in t.Tabs) Walk(tab);
                    break;
            }
        }
        void AddGroupId(string id) {
            if (!groupIds.Add(id)) throw new LayoutSpecException($"{typeof(T).Name}: group id '{id}' is used twice.");
        }
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
