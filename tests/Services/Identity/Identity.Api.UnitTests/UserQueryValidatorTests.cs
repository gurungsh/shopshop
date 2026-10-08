using Identity.Api.DTOs;
using Identity.Api.Validators;

namespace Identity.Api.UnitTests;

public class UserQueryValidatorTests
{
    private readonly UserQueryValidator _validator = new();

    #region Validate Tests

    [Fact]
    public void Validate_WithDefaults_ShouldBeValid()
    {
        // Act
        var result = _validator.Validate(new UserQuery());

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithPageBelowOne_ShouldBeInvalid(int page)
    {
        // Act
        var result = _validator.Validate(new UserQuery(Page: page));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UserQuery.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_WithPageSizeOutOfRange_ShouldBeInvalid(int pageSize)
    {
        // Act
        var result = _validator.Validate(new UserQuery(PageSize: pageSize));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UserQuery.PageSize));
    }

    [Fact]
    public void Validate_WithMaxPageSize_ShouldBeValid()
    {
        // Act
        var result = _validator.Validate(new UserQuery(PageSize: 100));

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion
}
