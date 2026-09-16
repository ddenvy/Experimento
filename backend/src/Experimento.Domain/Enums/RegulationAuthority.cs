namespace Experimento.Domain.Enums;

/// <summary>
/// Регуляторный орган / свод правил, налагающий ограничения на вещество.
/// Локальный справочник — расширяется по мере добавления новых источников.
/// </summary>
public enum RegulationAuthority
{
    /// <summary>REACH (ЕС) — Substances of Very High Concern (SVHC) список.</summary>
    ReachSvhc = 1,

    /// <summary>TSCA / EPA США — PFAS и другие ограниченные вещества.</summary>
    EpaPfas = 2,

    /// <summary>California Proposition 65 — вещества, известные как вызывающие рак/репродуктивный вред.</summary>
    CaliforniaProp65 = 3,

    /// <summary>VOC — летучие органические соединения с ограничениями по эмиссии.</summary>
    Voc = 4,
}
