using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Data;

public class PersonUserAccountShapeTests
{
    [Fact]
    public void Person_HasIdentityFields_NoCredentialFields()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Ann",
            LastName = "Silva",
            Email = "ann@example.com",
            Phone = "0771234567",
            Address = new Address { City = "Colombo" },
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Assert.Equal("Ann", person.FirstName);
        Assert.Null(person.GetType().GetProperty("PasswordHash"));
    }

    [Fact]
    public void UserAccount_RequiresPersonId_HasCredentialFields()
    {
        var personId = Guid.NewGuid();
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            PasswordHash = "hash",
            EmailVerified = true,
            HasCompletedSignup = true,
            FailedLoginAttempts = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Assert.Equal(personId, account.PersonId);
        Assert.Null(account.GetType().GetProperty("FirstName"));
    }
}
