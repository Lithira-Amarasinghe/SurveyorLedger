using SurveyorLedger.API.Controllers;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Controllers;

public class UserControllerMappingTests
{
    [Fact]
    public void ToResponse_MapsPersonAndAccountFieldsCorrectly()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Ann", LastName = "Silva", Email = "ann@test.local",
            Phone = "0771234567", Address = new Address { City = "Colombo" },
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true
        };
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), PersonId = person.Id, EmailVerified = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true
        };

        var response = UserController.ToResponse(person, account);

        Assert.Equal(account.Id, response.UserId);
        Assert.Equal("Ann", response.FirstName);
        Assert.True(response.EmailVerified);
    }
}
