using FluentAssertions;
using Simcag.Shared.Events;
using Simcag.Shared.Finance;

namespace Simcag.AIService.Tests;

public sealed class FinancialLineItemSemanticNormalizerTests
{
    [Fact]
    public void Repair_condominium_row_with_year_glued_qty_and_repeated_r_currency()
    {
        var r = FinancialLineItemSemanticNormalizer.Repair(
            "Taxa Condominial - Competência Maio/20261R$ 820,00R$",
            820m);
        r.CleanDescription.Should().Be("Taxa Condominial - Competência Maio/2026");
        r.LineTotal.Should().Be(820m);
        r.Quantity.Should().Be(1);
        r.UnitPrice.Should().Be(820m);
    }

    [Fact]
    public void Repair_qty_before_unit_and_line_totals()
    {
        var r = FinancialLineItemSemanticNormalizer.Repair(
            "Serviços de limpeza  3  R$ 150,00  R$ 450,00",
            450m);
        r.CleanDescription.Should().Be("Serviços de limpeza");
        r.Quantity.Should().Be(3);
        r.UnitPrice.Should().Be(150m);
    }

    [Fact]
    public void Repair_multiple_brl_blocks_and_zero_cent_tail()
    {
        var r = FinancialLineItemSemanticNormalizer.Repair(
            "Energia elétrica R$2.100,50 e taxa R$ 0,00",
            2100.50m);
        r.CleanDescription.Should().Be("Energia elétrica e taxa");
        r.Quantity.Should().BeNull();
        r.UnitPrice.Should().BeNull();
    }

    [Fact]
    public void Repair_preserves_slash_dates_when_masking()
    {
        var r = FinancialLineItemSemanticNormalizer.Repair(
            "Pagamento ref. 15/05/2026 R$ 99,90",
            99.90m);
        r.CleanDescription.Should().Contain("15/05/2026");
        r.CleanDescription.Should().NotContain("R$");
    }

    [Fact]
    public void Repair_preserves_quantity_when_unit_equals_line_total()
    {
        var r = FinancialLineItemSemanticNormalizer.Repair(
            "Camera IP Full HD 2MP",
            10680m,
            declaredQuantity: 12,
            declaredUnitPrice: 10680m);
        r.Quantity.Should().Be(12);
        r.UnitPrice.Should().BeNull();
    }

    [Fact]
    public void Repair_rejects_incoherent_declared_quantity_and_unit()
    {
        var r = FinancialLineItemSemanticNormalizer.Repair(
            "Item colado R$ 100,00",
            100m,
            declaredQuantity: 5,
            declaredUnitPrice: 99m);
        r.Quantity.Should().BeNull();
        r.UnitPrice.Should().BeNull();
    }

    [Fact]
    public void NormalizeFinancialItem_is_idempotent_for_clean_rows()
    {
        var clean = new FinancialItem
        {
            Description = "Fundo de reserva",
            Amount = 200m,
            Quantity = 1,
            UnitPrice = 200m
        };
        var twice = FinancialLineItemSemanticNormalizer.NormalizeFinancialItem(
            FinancialLineItemSemanticNormalizer.NormalizeFinancialItem(clean));
        twice.Description.Should().Be(clean.Description);
        twice.Amount.Should().Be(clean.Amount);
    }

    [Fact]
    public void ToSearchQueryLabel_truncates_on_word_boundary()
    {
        var longText = string.Join(" ", Enumerable.Repeat("manutenção predial", 20));
        var q = FinancialLineItemSemanticNormalizer.ToSearchQueryLabel(longText, maxLen: 48);
        q.Length.Should().BeLessThanOrEqualTo(48);
        q.Should().NotEndWith(" ");
    }
}
