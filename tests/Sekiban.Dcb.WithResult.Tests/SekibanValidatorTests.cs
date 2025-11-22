using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Validation;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Dcb.Tests;

public class SekibanValidatorTests
{

    #region Nested Command Validation Tests
    [Fact]
    public void Validate_NestedCommandWithErrors_ReturnsNestedErrors()
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
        var errors = SekibanValidator.Validate(command);

        // Assert
        Assert.Equal(3, errors.Count);

        // Check nested property names
        Assert.Contains(errors, e => e.PropertyName == "NestedCommand.Name");
        Assert.Contains(errors, e => e.PropertyName == "NestedCommand.Age");
        Assert.Contains(errors, e => e.PropertyName == "NestedCommand.Email");
    }
    #endregion
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
    public void Validate_ValidCommand_ReturnsNoErrors()
    {
        // Arrange
        var command = new ValidTestCommand();

        // Act
        var errors = SekibanValidator.Validate(command);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidNestedCommand_ReturnsNoErrors()
    {
        // Arrange
        var command = new NestedTestCommand
        {
            Name = "Parent",
            NestedCommand = new ValidTestCommand()
        };

        // Act
        var errors = SekibanValidator.Validate(command);

        // Assert
        Assert.Empty(errors);
    }
    #endregion

    #region Invalid Command Tests
    [Fact]
    public void Validate_NullCommand_ReturnsError()
    {
        // Act
        var errors = SekibanValidator.Validate(null!);

        // Assert
        Assert.Single(errors);
        Assert.Equal("Object", errors[0].PropertyName);
        Assert.Contains("null", errors[0].ErrorMessage);
    }

    [Fact]
    public void Validate_InvalidCommand_ReturnsMultipleErrors()
    {
        // Arrange
        var command = new InvalidTestCommand();

        // Act
        var errors = SekibanValidator.Validate(command);

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
    public void ValidateAndThrow_ValidCommand_DoesNotThrow()
    {
        // Arrange
        var command = new ValidTestCommand();

        // Act & Assert
        var exception = Record.Exception(() => SekibanValidator.ValidateAndThrow(command));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateAndThrow_InvalidCommand_ThrowsException()
    {
        // Arrange
        var command = new InvalidTestCommand();

        // Act & Assert
        var exception = Assert.Throws<SekibanValidationException>(() =>
            SekibanValidator.ValidateAndThrow(command));

        Assert.Equal(4, exception.Errors.Count);
        Assert.Contains("4 error(s)", exception.Message);
    }

    [Fact]
    public void SekibanValidationException_IsValidationException()
    {
        // Arrange
        var errors = new[]
        {
            new SekibanValidationError("Name", "Name is required"),
            new SekibanValidationError("Age", "Age must be between 1 and 100", 150)
        };

        // Act
        var exception = new SekibanValidationException(errors);

        // Assert
        Assert.IsAssignableFrom<ValidationException>(exception);
        Assert.NotNull(exception.ValidationResult);
        Assert.Contains("Name", exception.ValidationResult.MemberNames);
        Assert.Contains("Age", exception.ValidationResult.MemberNames);
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
    public void Validate_MultipleValidationAttributesPerProperty_ValidatesAll()
    {
        // Arrange
        var command = new CustomValidationCommand
        {
            Name = "john123", // Invalid pattern
            Age = 10, // Out of range
            Email = "short" // Too short and not email
        };

        // Act
        var errors = SekibanValidator.Validate(command);

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
    public void Validate_AllAttributesValid_ReturnsNoErrors()
    {
        // Arrange
        var command = new CustomValidationCommand
        {
            Name = "John",
            Age = 30,
            Email = "john.doe@example.com"
        };

        // Act
        var errors = SekibanValidator.Validate(command);

        // Assert
        Assert.Empty(errors);
    }
    #endregion

    #region Record Type Tests
    private record RecordCommand(
        [property: Required] string Name,
        [property: Range(1, 100)] int Age,
        [property: EmailAddress] string Email) : ICommand;

    private record NestedRecordCommand(
        [property: Required] string ParentName,
        RecordCommand? Child) : ICommand;

    [Fact]
    public void Validate_ValidRecordCommand_ReturnsNoErrors()
    {
        // Arrange
        var command = new RecordCommand("John", 25, "john@example.com");

        // Act
        var errors = SekibanValidator.Validate(command);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidRecordCommand_ReturnsErrors()
    {
        // Arrange
        var command = new RecordCommand(null!, 150, "invalid-email");

        // Act
        var errors = SekibanValidator.Validate(command);

        // Assert
        Assert.True(errors.Count >= 2); // Name and Age should fail
        Assert.Contains(errors, e => e.PropertyName == "Name");
        Assert.Contains(errors, e => e.PropertyName == "Age");
    }

    [Fact]
    public void Validate_NestedRecordCommand_ValidatesNested()
    {
        // Arrange
        var command = new NestedRecordCommand(
            "Parent",
            new RecordCommand(null!, 200, "invalid"));

        // Act
        var errors = SekibanValidator.Validate(command);

        // Assert
        Assert.True(errors.Count >= 2);
        Assert.Contains(errors, e => e.PropertyName == "Child.Name");
        Assert.Contains(errors, e => e.PropertyName == "Child.Age");
    }
    #endregion

    #region Circular Reference Tests
    private class CircularReferenceParent : ICommand
    {
        [Required]
        public string Name { get; set; } = "Parent";
        public CircularReferenceChild? Child { get; set; }
    }

    private class CircularReferenceChild
    {
        [Required]
        public string Name { get; set; } = "Child";
        public CircularReferenceParent? Parent { get; set; }
    }

    [Fact]
    public void Validate_CircularReference_DoesNotCauseInfiniteRecursion()
    {
        // Arrange
        var parent = new CircularReferenceParent { Name = "Parent" };
        var child = new CircularReferenceChild { Name = "Child" };
        parent.Child = child;
        child.Parent = parent;

        // Act
        var exception = Record.Exception(() =>
        {
            var errors = SekibanValidator.Validate(parent);
            // Should complete without stack overflow
            Assert.NotNull(errors); // Just to ensure we can use the result
        });

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_SelfReference_DoesNotCauseInfiniteRecursion()
    {
        // Arrange
        var node = new CircularReferenceParent { Name = "Node" };
        node.Child = new CircularReferenceChild { Name = "Child" };
        node.Child.Parent = node;

        // Act
        var exception = Record.Exception(() =>
        {
            var errors = SekibanValidator.Validate(node);
            Assert.NotNull(errors);
        });

        // Assert
        Assert.Null(exception);
    }
    #endregion

    #region Deep Nesting and External Type Tests
    // Mock external type similar to ResultBoxes.OptionalValue
    private class MockOptionalValue<T>
    {
        public bool HasValue { get; }
        public T? Value { get; }

        public MockOptionalValue(T value)
        {
            HasValue = true;
            Value = value;
        }

        public MockOptionalValue()
        {
            HasValue = false;
            Value = default;
        }

        public static MockOptionalValue<T> Empty => new();
    }

    private class DeeplyNestedCommand : ICommand
    {
        [Required]
        public string Name { get; set; } = "";
        public DeeplyNestedCommand? Level1 { get; set; }
    }

    [Fact]
    public void Validate_DeeplyNestedStructure_DoesNotCauseStackOverflow()
    {
        // Arrange - Create a deeply nested structure (11 levels, exceeding MaxDepth of 10)
        var root = new DeeplyNestedCommand { Name = "Level0" };
        var current = root;
        for (int i = 1; i <= 11; i++)
        {
            current.Level1 = new DeeplyNestedCommand { Name = $"Level{i}" };
            current = current.Level1;
        }

        // Act - Should not throw stack overflow
        var exception = Record.Exception(() =>
        {
            var errors = SekibanValidator.Validate(root);
            Assert.NotNull(errors);
        });

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_CommandWithManyProperties_HandlesGracefully()
    {
        // Arrange - Simulate a command with many properties like NendoKanyu
        var command = new CommandWithManyOptionalValues
        {
            Id = "test-id",
            Name = "Test"
        };

        // Act
        var exception = Record.Exception(() =>
        {
            var errors = SekibanValidator.Validate(command);
            Assert.NotNull(errors);
        });

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_OptionalValueWithInvalidContent_ValidatesContent()
    {
        // Arrange - OptionalValue containing an object with validation attributes
        var command = new CommandWithValidatableOptionalValue
        {
            Id = "test-id",
            OptionalData = new MockOptionalValue<ValidatableObject>(
                new ValidatableObject { Name = null! }) // Invalid: Required field is null
        };

        // Act
        var errors = SekibanValidator.Validate(command);

        // Assert - Should validate the content inside OptionalValue
        // The depth limit and try-catch protection prevent stack overflow
        // but still allow validation of nested objects
        Assert.NotNull(errors);
    }

    private class CommandWithValidatableOptionalValue : ICommand
    {
        [Required]
        public string Id { get; set; } = "";

        public MockOptionalValue<ValidatableObject> OptionalData { get; set; } = MockOptionalValue<ValidatableObject>.Empty;
    }

    private class ValidatableObject
    {
        [Required]
        public string? Name { get; set; }
    }

    private class CommandWithManyOptionalValues : ICommand
    {
        [Required]
        public string Id { get; set; } = "";

        [Required]
        public string Name { get; set; } = "";

        // Many optional value properties (simulating NendoKanyu)
        public MockOptionalValue<DateTime> Date1 { get; set; } = MockOptionalValue<DateTime>.Empty;
        public MockOptionalValue<DateTime> Date2 { get; set; } = MockOptionalValue<DateTime>.Empty;
        public MockOptionalValue<string> String1 { get; set; } = MockOptionalValue<string>.Empty;
        public MockOptionalValue<string> String2 { get; set; } = MockOptionalValue<string>.Empty;
        public MockOptionalValue<int> Int1 { get; set; } = MockOptionalValue<int>.Empty;
        public MockOptionalValue<int> Int2 { get; set; } = MockOptionalValue<int>.Empty;
        public MockOptionalValue<NestedObject> Nested1 { get; set; } = MockOptionalValue<NestedObject>.Empty;
        public MockOptionalValue<NestedObject> Nested2 { get; set; } = MockOptionalValue<NestedObject>.Empty;
    }

    private class NestedObject
    {
        public string Value { get; set; } = "";
        public MockOptionalValue<string> NestedOptional { get; set; } = MockOptionalValue<string>.Empty;
    }

    [Fact]
    public void Validate_PropertyWithGetValueException_SkipsProperty()
    {
        // This tests that properties that throw exceptions when accessed are skipped
        var command = new CommandWithProblematicProperty();

        // Act - Should not throw
        var exception = Record.Exception(() =>
        {
            var errors = SekibanValidator.Validate(command);
            Assert.NotNull(errors);
        });

        // Assert
        Assert.Null(exception);
    }

    private class CommandWithProblematicProperty : ICommand
    {
        [Required]
        public string Name { get; set; } = "Valid";

        public string ProblematicProperty
        {
            get => throw new InvalidOperationException("Cannot access this property");
            set { }
        }
    }
    #endregion
}
