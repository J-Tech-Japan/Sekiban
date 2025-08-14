using System.ComponentModel.DataAnnotations;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Validation;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class CommandValidatorTests
{
    #region Test Command Classes
    
    private class ValidTestCommand : ICommand
    {
        [Required]
        public string Name { get; set; } = "ValidName";

        [Range(1, 100)]
        public int Age { get; set; } = 25;

        [EmailAddress]
        public string Email { get; set; } = "test@example.com";
    }

    private class InvalidTestCommand : ICommand
    {
        [Required(ErrorMessage = "Name is required")]
        public string? Name { get; set; }

        [Range(1, 100, ErrorMessage = "Age must be between 1 and 100")]
        public int Age { get; set; } = 150;

        [StringLength(10, MinimumLength = 5, ErrorMessage = "Code must be between 5 and 10 characters")]
        public string Code { get; set; } = "abc";

        [EmailAddress(ErrorMessage = "Email must be a valid email address")]
        public string Email { get; set; } = "notanemail";
    }

    private class NestedTestCommand : ICommand
    {
        [Required]
        public string Name { get; set; } = "Parent";

        public ValidTestCommand? NestedCommand { get; set; }
    }

    #endregion

    #region Valid Command Tests

    [Fact]
    public void ValidateCommand_ValidCommand_ReturnsNoErrors()
    {
        // Arrange
        var command = new ValidTestCommand();

        // Act
        var errors = CommandValidator.ValidateCommand(command);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCommand_ValidNestedCommand_ReturnsNoErrors()
    {
        // Arrange
        var command = new NestedTestCommand
        {
            Name = "Parent",
            NestedCommand = new ValidTestCommand()
        };

        // Act
        var errors = CommandValidator.ValidateCommand(command);

        // Assert
        Assert.Empty(errors);
    }

    #endregion

    #region Invalid Command Tests

    [Fact]
    public void ValidateCommand_NullCommand_ReturnsError()
    {
        // Act
        var errors = CommandValidator.ValidateCommand(null!);

        // Assert
        Assert.Single(errors);
        Assert.Equal("Command", errors[0].PropertyName);
        Assert.Contains("null", errors[0].ErrorMessage);
    }

    [Fact]
    public void ValidateCommand_InvalidCommand_ReturnsMultipleErrors()
    {
        // Arrange
        var command = new InvalidTestCommand();

        // Act
        var errors = CommandValidator.ValidateCommand(command);

        // Assert
        Assert.Equal(4, errors.Count);
        
        // Check for Name required error
        var nameError = errors.FirstOrDefault(e => e.PropertyName == "Name");
        Assert.NotNull(nameError);
        Assert.Equal("Name is required", nameError.ErrorMessage);

        // Check for Age range error
        var ageError = errors.FirstOrDefault(e => e.PropertyName == "Age");
        Assert.NotNull(ageError);
        Assert.Equal("Age must be between 1 and 100", ageError.ErrorMessage);
        Assert.Equal(150, ageError.AttemptedValue);

        // Check for Code length error
        var codeError = errors.FirstOrDefault(e => e.PropertyName == "Code");
        Assert.NotNull(codeError);
        Assert.Equal("Code must be between 5 and 10 characters", codeError.ErrorMessage);

        // Check for Email format error
        var emailError = errors.FirstOrDefault(e => e.PropertyName == "Email");
        Assert.NotNull(emailError);
        Assert.Equal("Email must be a valid email address", emailError.ErrorMessage);
    }

    #endregion

    #region Exception Throwing Tests

    [Fact]
    public void ValidateCommandAndThrow_ValidCommand_DoesNotThrow()
    {
        // Arrange
        var command = new ValidTestCommand();

        // Act & Assert
        var exception = Record.Exception(() => CommandValidator.ValidateCommandAndThrow(command));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateCommandAndThrow_InvalidCommand_ThrowsException()
    {
        // Arrange
        var command = new InvalidTestCommand();

        // Act & Assert
        var exception = Assert.Throws<CommandValidationException>(() => 
            CommandValidator.ValidateCommandAndThrow(command));
        
        Assert.Equal(4, exception.Errors.Count);
        Assert.Contains("4 error(s)", exception.Message);
    }

    #endregion

    #region Nested Command Validation Tests

    [Fact]
    public void ValidateCommand_NestedCommandWithErrors_ReturnsNestedErrors()
    {
        // Arrange
        var command = new NestedTestCommand
        {
            Name = "Parent",
            NestedCommand = new ValidTestCommand
            {
                Name = null!, // Invalid
                Age = 200, // Invalid
                Email = "invalid" // Invalid
            }
        };

        // Act
        var errors = CommandValidator.ValidateCommand(command);

        // Assert
        Assert.Equal(3, errors.Count);
        
        // Check nested property names
        Assert.Contains(errors, e => e.PropertyName == "NestedCommand.Name");
        Assert.Contains(errors, e => e.PropertyName == "NestedCommand.Age");
        Assert.Contains(errors, e => e.PropertyName == "NestedCommand.Email");
    }

    #endregion

    #region Integration with Validation Attributes

    private class CustomValidationCommand : ICommand
    {
        [Required]
        [RegularExpression(@"^[A-Z][a-z]+$", ErrorMessage = "Name must start with uppercase and contain only letters")]
        public string Name { get; set; } = "";

        [Range(18, 65, ErrorMessage = "Age must be between 18 and 65")]
        public int Age { get; set; }

        [StringLength(50, MinimumLength = 10)]
        [EmailAddress]
        public string Email { get; set; } = "";
    }

    [Fact]
    public void ValidateCommand_MultipleValidationAttributesPerProperty_ValidatesAll()
    {
        // Arrange
        var command = new CustomValidationCommand
        {
            Name = "john123", // Invalid pattern
            Age = 10, // Out of range
            Email = "short" // Too short and not email
        };

        // Act
        var errors = CommandValidator.ValidateCommand(command);

        // Assert
        Assert.True(errors.Count >= 3);
        
        // Check that pattern validation is triggered
        var nameError = errors.FirstOrDefault(e => e.PropertyName == "Name");
        Assert.NotNull(nameError);
        Assert.Contains("uppercase", nameError.ErrorMessage);

        // Check age validation
        var ageError = errors.FirstOrDefault(e => e.PropertyName == "Age");
        Assert.NotNull(ageError);
        Assert.Contains("18 and 65", ageError.ErrorMessage);
    }

    [Fact]
    public void ValidateCommand_AllAttributesValid_ReturnsNoErrors()
    {
        // Arrange
        var command = new CustomValidationCommand
        {
            Name = "John",
            Age = 30,
            Email = "john.doe@example.com"
        };

        // Act
        var errors = CommandValidator.ValidateCommand(command);

        // Assert
        Assert.Empty(errors);
    }

    #endregion
}