using Ordering.Api.DTOs;
using Ordering.Api.Validators;

namespace Ordering.Api.UnitTests;

public class OrderQueryValidatorTests
{
    private readonly OrderQueryValidator _validator = new();

    #region Validate Tests

    [Fact]
    public void Validate_WithDefaultsAndMaxPageSize_ShouldBeValid()
    {
        // Act
        var defaults = _validator.Validate(new OrderQuery());
        var maxPageSize = _validator.Validate(new OrderQuery(PageSize: 100));

        // Assert
        Assert.True(defaults.IsValid);
        Assert.True(maxPageSize.IsValid);
    }

    [Theory]
    [InlineData(0, 20, nameof(OrderQuery.Page))]
    [InlineData(-1, 20, nameof(OrderQuery.Page))]
    [InlineData(1, 0, nameof(OrderQuery.PageSize))]
    [InlineData(1, 101, nameof(OrderQuery.PageSize))]
    public void Validate_WithPagingOutOfRange_ShouldBeInvalid(int page, int pageSize, string invalidProperty)
    {
        // Act
        var result = _validator.Validate(new OrderQuery(Page: page, PageSize: pageSize));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == invalidProperty);
    }

    [Theory]
    [MemberData(nameof(InvalidRanges))]
    public void Validate_WithInvertedRange_ShouldBeInvalid(OrderQuery query, string invalidProperty)
    {
        // Act
        var result = _validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == invalidProperty);
    }

    public static TheoryData<OrderQuery, string> InvalidRanges() => new()
    {
        { new OrderQuery(MinTotalAmount: 50m, MaxTotalAmount: 10m), nameof(OrderQuery.MaxTotalAmount) },
        { new OrderQuery(MinItemCount: 5, MaxItemCount: 1), nameof(OrderQuery.MaxItemCount) },
        { new OrderQuery(CreatedAfterUtc: new DateTime(2026, 2, 1), CreatedBeforeUtc: new DateTime(2026, 1, 1)), nameof(OrderQuery.CreatedAfterUtc) },
        { new OrderQuery(UpdatedAfterUtc: new DateTime(2026, 2, 1), UpdatedBeforeUtc: new DateTime(2026, 1, 1)), nameof(OrderQuery.UpdatedAfterUtc) },
    };

    [Fact]
    public void Validate_WithEqualRangeBounds_ShouldBeValid()
    {
        // Act
        var result = _validator.Validate(new OrderQuery(
            MinTotalAmount: 10m, MaxTotalAmount: 10m,
            MinItemCount: 2, MaxItemCount: 2,
            CreatedAfterUtc: new DateTime(2026, 1, 1), CreatedBeforeUtc: new DateTime(2026, 1, 1)));

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion
}
