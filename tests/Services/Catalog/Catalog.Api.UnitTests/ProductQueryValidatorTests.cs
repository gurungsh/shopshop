using Catalog.Api.DTOs;
using Catalog.Api.Validators;

namespace Catalog.Api.UnitTests;

public class ProductQueryValidatorTests
{
    private readonly ProductQueryValidator _validator = new();

    #region Validate Tests

    [Fact]
    public void Validate_WithDefaults_ShouldBeValid()
    {
        // Act
        var result = _validator.Validate(new ProductQuery());

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithPageBelowOne_ShouldBeInvalid(int page)
    {
        // Act
        var result = _validator.Validate(new ProductQuery(Page: page));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductQuery.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_WithPageSizeOutOfRange_ShouldBeInvalid(int pageSize)
    {
        // Act
        var result = _validator.Validate(new ProductQuery(PageSize: pageSize));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductQuery.PageSize));
    }

    [Fact]
    public void Validate_WithMaxPageSize_ShouldBeValid()
    {
        // Act
        var result = _validator.Validate(new ProductQuery(PageSize: 100));

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion
}
