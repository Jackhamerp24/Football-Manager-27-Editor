using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>The Export button and CLI write these bytes; the app sets <see cref="PlayerRecord.GameDate"/> before
/// exporting, which is what makes the Age column non-empty.</summary>
public class PlayerExportTests
{
    static PlayerRecord Player(DateOnly? gameDate) => new()
    {
        Address = 0x1000_0000, VtableRva = 0x4D27228, Id = 43124203,
        FirstName = "Bruno", LastName = "Fernandes", FullName = "Bruno Miguel Borges Fernandes",
        DateOfBirth = new DateOnly(1994, 9, 8), CurrentAbility = 177, PotentialAbility = 178,
        HomeReputation = 8991, CurrentReputation = 8960, WorldReputation = 8154, Height = 179,
        RawAttributes = new byte[Enum.GetValues<PlayerAttribute>().Length],
        AttributeScale = 5, Positions = new byte[Enum.GetValues<PlayerPosition>().Length],
        Personality = new byte[Enum.GetValues<PersonalityAttribute>().Length], GameDate = gameDate,
    };

    static string Csv(PlayerRecord player)
    {
        using var stream = new MemoryStream();
        PlayerExport.WriteCsv(stream, [player]);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Csv_carries_age_and_game_date_when_the_caller_sets_the_game_date()
    {
        string text = Csv(Player(gameDate: new DateOnly(2026, 11, 21)));
        string[] lines = text.TrimEnd().Split("\r\n");
        Assert.Equal(2, lines.Length);
        string[] head = lines[0].Split(',');
        string[] row = lines[1].Split(',');
        int age = Array.IndexOf(head, "Age"), gameDate = Array.IndexOf(head, "GameDate");
        Assert.True(age >= 0 && gameDate >= 0);
        Assert.Equal("32", row[age]);
        Assert.Equal("2026-11-21", row[gameDate]);
    }

    [Fact]
    public void Csv_leaves_age_empty_without_a_game_date_and_never_writes_an_address()
    {
        string text = Csv(Player(gameDate: null));
        string[] lines = text.TrimEnd().Split("\r\n");
        string[] head = lines[0].Split(',');
        string[] row = lines[1].Split(',');
        Assert.Equal("", row[Array.IndexOf(head, "Age")]);
        Assert.DoesNotContain("0x1000", text);
        Assert.Contains("Bruno Fernandes", text);
    }
}
