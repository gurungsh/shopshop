using Ordering.Api.DTOs;
using Ordering.Api.Validators;

namespace Ordering.Api.UnitTests;

public class OrderQueryValidatorTests
{
    private readonly OrderQueryValidator _validator = new();

    #region Validate Tests

    [Fact]
    public void Validate_WithDefaults_ShouldBeValid()
    {
        // Act
        var result = _validator.Validate(new OrderQuery());

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithPageBelowOne_ShouldBeInvalid(int page)
    {
        // Act
        var result = _validator.Validate(new OrderQuery(Page: page));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(OrderQuery.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_WithPageSizeOutOfRange_ShouldBeInvalid(int pageSize)
    {
        // Act
        var result = _validator.Validate(new OrderQuery(PageSize: pageSize));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(OrderQuery.PageSize));
    }

    [Fact]
    public void Validate_WithMaxPageSize_ShouldBeValid()
    {
        // Act
        var result = _validator.Validate(new OrderQuery(PageSize: 100));

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion
}
