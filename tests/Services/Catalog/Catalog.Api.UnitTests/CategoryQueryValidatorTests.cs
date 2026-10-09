using Catalog.Api.DTOs;
using Catalog.Api.Validators;

namespace Catalog.Api.UnitTests;

public class CategoryQueryValidatorTests
{
    private readonly CategoryQueryValidator _validator = new();

    #region Validate Tests

    [Fact]
    public void Validate_WithDefaultsAndMaxPageSize_ShouldBeValid()
    {
        // Act
        var defaults = _validator.Validate(new CategoryQuery());
        var maxPageSize = _validator.Validate(new CategoryQuery(PageSize: 100));

        // Assert
        Assert.True(defaults.IsValid);
        Assert.True(maxPageSize.IsValid);
    }

    [Theory]
    [InlineData(0, 20, nameof(CategoryQuery.Page))]
    [InlineData(-1, 20, nameof(CategoryQuery.Page))]
    [InlineData(1, 0, nameof(CategoryQuery.PageSize))]
    [InlineData(1, 101, nameof(CategoryQuery.PageSize))]
    public void Validate_WithPagingOutOfRange_ShouldBeInvalid(int page, int pageSize, string invalidProperty)
    {
        // Act
        var result = _validator.Validate(new CategoryQuery(Page: page, PageSize: pageSize));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == invalidProperty);
    }

    #endregion
}
