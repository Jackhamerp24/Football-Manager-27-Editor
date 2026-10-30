using System.Globalization;
using System.Text.RegularExpressions;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public enum FieldKind { Rating20, Int, Text, Date, ReadOnly }
public sealed record FieldDescriptor<T>(string Id, string Label, string Group, FieldKind Kind, int Min, int Max, Func<T, int> Read, Action<PlayerWriter, T, int>? Write);
public sealed record ColumnDescriptor(string Title, string Binding);
public interface IEditorModule
{
    string Title { get; }
    IReadOnlyList<ColumnDescriptor> Columns { get; }
    Task<IReadOnlyList<object>> LoadAsync(IEditorBackend backend, CancellationToken ct);
    bool Matches(object row, string query);
}
public interface IEntityAction
{
    string Title { get; }
    Type EntityType { get; }
    bool CanRun(object entity);
    Task RunAsync(IEditService edits, IReadOnlyList<object> entities, CancellationToken ct);
}
public interface IBackgroundService { Task RunAsync(CancellationToken ct); }
public static class Modules
{
    public static IReadOnlyList<IEditorModule> Registered { get; } = [new PlayersModule()];
    public static IReadOnlyList<IEntityAction> Actions { get; } = [];
}
public sealed class PlayersModule : IEditorModule
{
    static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    public string Title => "Players";
    public IReadOnlyList<ColumnDescriptor> Columns { get; } = [new("Name", "DisplayName"), new("Born", "DateOfBirth.Year"), new("CA", "CurrentAbility"), new("PA", "PotentialAbility")];
    public async Task<IReadOnlyList<object>> LoadAsync(IEditorBackend backend, CancellationToken ct) =>
        await Task.Run<IReadOnlyList<object>>(() => backend.Players.LoadAll().OrderByDescending(p => p.CurrentAbility).Cast<object>().ToArray(), ct);
    public bool Matches(object row, string query)
    {
        var p = (PlayerRecord)row;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return Compare.IndexOf(p.DisplayName, query, options) >= 0 || Compare.IndexOf(p.FullName ?? "", query, options) >= 0;
    }
}
public static class PlayerFields
{
    static string Label(string s) => s switch
    {
        "FreeKicks" => "Free Kick Taking", "TendencyToPunch" => "Punching (Tendency)", "OneOnOnes" => "One On Ones",
        _ => Regex.Replace(s, "(?<!^)([A-Z])", " $1")
    };
    public static IReadOnlyList<FieldDescriptor<PlayerRecord>> All { get; } = Create();
    static IReadOnlyList<FieldDescriptor<PlayerRecord>> Create()
    {
        var fields = new List<FieldDescriptor<PlayerRecord>> {
            new("CA", "CA", "Ability", FieldKind.Int, 1, 200, p => p.CurrentAbility, (w,p,v) => w.SetCurrentAbility(p,v)),
            new("PA", "PA", "Ability", FieldKind.Int, 1, 200, p => p.PotentialAbility, (w,p,v) => w.SetPotentialAbility(p,v)),
            new("HomeReputation", "Home", "Reputation", FieldKind.ReadOnly, 0, 10000, p => p.HomeReputation, null),
            new("CurrentReputation", "Current", "Reputation", FieldKind.ReadOnly, 0, 10000, p => p.CurrentReputation, null),
            new("WorldReputation", "World", "Reputation", FieldKind.ReadOnly, 0, 10000, p => p.WorldReputation, null)
        };
        foreach (var a in Enum.GetValues<PlayerAttribute>()) fields.Add(new(a.ToString(), Label(a.ToString()), PlayerAttributeInfo.GroupOf(a).ToString(), FieldKind.Rating20, 1, 20, p => p.Attribute(a), (w,p,v) => w.SetAttribute(p,a,v)));
        foreach (var a in Enum.GetValues<PersonalityAttribute>()) fields.Add(new(a.ToString(), Label(a.ToString()), "Personality", FieldKind.Rating20, 1, 20, p => p.PersonalityValue(a), (w,p,v) => w.SetPersonality(p,a,v)));
        foreach (var a in Enum.GetValues<PlayerPosition>()) fields.Add(new(a.ToString(), Label(a.ToString()), "Positions", FieldKind.Rating20, 1, 20, p => p.Position(a), (w,p,v) => w.SetPosition(p,a,v)));
        return fields;
    }
}
