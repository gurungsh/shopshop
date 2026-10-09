using Identity.Api.DTOs;
using Identity.Api.Validators;

namespace Identity.Api.UnitTests;

public class UserQueryValidatorTests
{
    private readonly UserQueryValidator _validator = new();

    #region Validate Tests

    [Fact]
    public void Validate_WithDefaultsAndMaxPageSize_ShouldBeValid()
    {
        // Act
        var defaults = _validator.Validate(new UserQuery());
        var maxPageSize = _validator.Validate(new UserQuery(PageSize: 100));

        // Assert
        Assert.True(defaults.IsValid);
        Assert.True(maxPageSize.IsValid);
    }

    [Theory]
    [InlineData(0, 20, nameof(UserQuery.Page))]
    [InlineData(-1, 20, nameof(UserQuery.Page))]
    [InlineData(1, 0, nameof(UserQuery.PageSize))]
    [InlineData(1, 101, nameof(UserQuery.PageSize))]
    public void Validate_WithPagingOutOfRange_ShouldBeInvalid(int page, int pageSize, string invalidProperty)
    {
        // Act
        var result = _validator.Validate(new UserQuery(Page: page, PageSize: pageSize));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == invalidProperty);
    }

    #endregion
}
