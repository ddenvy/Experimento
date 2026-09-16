using Experimento.Domain.Entities;

namespace Experimento.Domain.Tests;

/// <summary>
/// Tests for the audit hash-chain integrity (ALCOA++ traceability).
/// </summary>
public class AuditEntryTests
{
    [Fact]
    public void Create_HashIsValidForFreshEntry()
    {
        var entry = AuditEntry.Create(1, Guid.NewGuid(), "Action", "Entity", "1", "{}", "prev");
        Assert.True(entry.IsHashValid());
    }

    [Fact]
    public void IsHashValid_ReturnsFalseWhenPayloadTampered()
    {
        var entry = AuditEntry.Create(1, Guid.NewGuid(), "Action", "Entity", "1", "{}", "prev");
        entry.PayloadJson = "{\"tampered\":true}";
        Assert.False(entry.IsHashValid());
    }

    [Fact]
    public void IsHashValid_ReturnsFalseWhenPreviousHashTampered()
    {
        var entry = AuditEntry.Create(1, Guid.NewGuid(), "Action", "Entity", "1", "{}", "prev");
        entry.PreviousHash = "hacked";
        Assert.False(entry.IsHashValid());
    }

    [Fact]
    public void Create_LinksToPreviousHash()
    {
        var prev = AuditEntry.Create(1, null, "A", "E", "1", "{}", "");
        var next = AuditEntry.Create(2, null, "B", "E", "2", "{}", prev.EntryHash);
        Assert.Equal(prev.EntryHash, next.PreviousHash);
        Assert.True(next.IsHashValid());
    }
}

/// <summary>
/// Tests for FormulationVersion validation rules.
/// </summary>
public class FormulationVersionTests
{
    [Fact]
    public void EnsureValid_ThrowsWhenNoComponents()
    {
        var v = new FormulationVersion();
        Assert.Throws<InvalidOperationException>(() => v.EnsureValid());
    }

    [Fact]
    public void EnsureValid_ThrowsWhenProportionSumNotOne()
    {
        var v = new FormulationVersion();
        v.Components.Add(new FormulationComponent { ChemicalName = "A", MolarMass = 100, Proportion = 0.3 });
        v.Components.Add(new FormulationComponent { ChemicalName = "B", MolarMass = 100, Proportion = 0.3 });
        Assert.Throws<InvalidOperationException>(() => v.EnsureValid());
    }

    [Fact]
    public void EnsureValid_ThrowsWhenMolarMassNonPositive()
    {
        var v = new FormulationVersion();
        v.Components.Add(new FormulationComponent { ChemicalName = "A", MolarMass = 0, Proportion = 1.0 });
        Assert.Throws<InvalidOperationException>(() => v.EnsureValid());
    }

    [Fact]
    public void EnsureValid_SucceedsForValidComposition()
    {
        var v = new FormulationVersion();
        v.Components.Add(new FormulationComponent { ChemicalName = "A", MolarMass = 100, Proportion = 0.6 });
        v.Components.Add(new FormulationComponent { ChemicalName = "B", MolarMass = 200, Proportion = 0.4 });
        v.EnsureValid();
    }
}
