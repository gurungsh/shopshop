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

    #endregion
}
