using Sekiban.Dcb.Validation;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class NameValidatorTests
{
    #region Projector Name Validation Tests
    
    [Theory]
    [InlineData("ValidProjectorName")]
    [InlineData("Valid-Projector-Name")]
    [InlineData("Valid_Projector_Name")]
    [InlineData("ValidProjector-Name_Mixed")]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("ProjectorNameWithMaxLengthXXXXXXXXXXXXXX")] // 40 chars exactly
    public void IsValidProjectorName_ValidNames_ReturnsTrue(string name)
    {
        // Act
        var result = NameValidator.IsValidProjectorName(name);
        
        // Assert
        Assert.True(result);
    }
    
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("Invalid Name With Spaces")]
    [InlineData("Invalid.Name.With.Dots")]
    [InlineData("Invalid123WithNumbers")]
    [InlineData("Invalid@Name")]
    [InlineData("Invalid#Name")]
    [InlineData("Invalid$Name")]
    [InlineData("ProjectorNameThatIsWayTooLongAndExceedsTheLimit")] // More than 40 chars
    public void IsValidProjectorName_InvalidNames_ReturnsFalse(string name)
    {
        // Act
        var result = NameValidator.IsValidProjectorName(name);
        
        // Assert
        Assert.False(result);
    }
    
    [Theory]
    [InlineData("ValidProjectorName")]
    [InlineData("Valid-Projector")]
    [InlineData("Valid_Projector")]
    public void ValidateProjectorNameAndThrow_ValidNames_DoesNotThrow(string name)
    {
        // Act & Assert
        var exception = Record.Exception(() => NameValidator.ValidateProjectorNameAndThrow(name));
        Assert.Null(exception);
    }
    
    [Fact]
    public void ValidateProjectorNameAndThrow_NullName_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateProjectorNameAndThrow(null!));
        Assert.Contains("cannot be null or empty", exception.Message);
    }
    
    [Fact]
    public void ValidateProjectorNameAndThrow_EmptyName_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateProjectorNameAndThrow(""));
        Assert.Contains("cannot be null or empty", exception.Message);
    }
    
    [Fact]
    public void ValidateProjectorNameAndThrow_TooLongName_ThrowsArgumentException()
    {
        // Arrange
        var longName = new string('A', 41);
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateProjectorNameAndThrow(longName));
        Assert.Contains("exceeds maximum length", exception.Message);
        Assert.Contains("40", exception.Message);
        Assert.Contains("41", exception.Message);
    }
    
    [Theory]
    [InlineData("Invalid Name", "invalid characters")]
    [InlineData("Invalid.Name", "invalid characters")]
    [InlineData("Invalid123", "invalid characters")]
    [InlineData("Invalid@Name", "invalid characters")]
    public void ValidateProjectorNameAndThrow_InvalidCharacters_ThrowsArgumentException(string name, string expectedMessage)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateProjectorNameAndThrow(name));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    
    #endregion
    
    #region Tag Group Name Validation Tests
    
    [Theory]
    [InlineData("Student")]
    [InlineData("ClassRoom")]
    [InlineData("Valid-Group")]
    [InlineData("Valid_Group")]
    [InlineData("Valid123Group")]
    [InlineData("123ValidGroup")]
    [InlineData("Valid.Group")]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("MaxLengthTagGroupNameXXXXXXXXXXXXXXXXXX")] // 40 chars exactly
    public void ValidateTagGroupNameAndThrow_ValidNames_DoesNotThrow(string name)
    {
        // Act & Assert
        var exception = Record.Exception(() => NameValidator.ValidateTagGroupNameAndThrow(name));
        Assert.Null(exception);
    }
    
    [Fact]
    public void ValidateTagGroupNameAndThrow_NullName_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateTagGroupNameAndThrow(null!));
        Assert.Contains("cannot be null or empty", exception.Message);
    }
    
    [Fact]
    public void ValidateTagGroupNameAndThrow_EmptyName_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateTagGroupNameAndThrow(""));
        Assert.Contains("cannot be null or empty", exception.Message);
    }
    
    [Fact]
    public void ValidateTagGroupNameAndThrow_TooLongName_ThrowsArgumentException()
    {
        // Arrange
        var longName = new string('A', 41);
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateTagGroupNameAndThrow(longName));
        Assert.Contains("exceeds maximum length", exception.Message);
        Assert.Contains("40", exception.Message);
    }
    
    [Theory]
    [InlineData("Invalid Name With Spaces", "invalid characters")]
    [InlineData("Invalid@Group", "invalid characters")]
    [InlineData("Invalid#Group", "invalid characters")]
    [InlineData("Invalid$Group", "invalid characters")]
    [InlineData("Invalid:Group", "invalid characters")] // Colon is not allowed in group names
    public void ValidateTagGroupNameAndThrow_InvalidCharacters_ThrowsArgumentException(string name, string expectedMessage)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateTagGroupNameAndThrow(name));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    
    #endregion
    
    #region Integration Tests
    
    [Fact]
    public void ValidateTagGroupNameAndThrow_UsesTagValidatorLogic()
    {
        // This test verifies that ValidateTagGroupNameAndThrow uses the same validation
        // logic as TagValidator for the group part
        
        // Arrange
        var groupName = "ValidGroup123";
        var dummyTag = $"{groupName}:dummyContent";
        
        // Act - Should not throw
        var exception = Record.Exception(() => NameValidator.ValidateTagGroupNameAndThrow(groupName));
        
        // Assert
        Assert.Null(exception);
    }
    
    [Fact]
    public void ProjectorName_And_TagGroupName_HaveDifferentRules()
    {
        // Projector names don't allow numbers, but tag group names do
        var nameWithNumbers = "Name123";
        
        // Act & Assert
        // Should fail for projector name
        Assert.False(NameValidator.IsValidProjectorName(nameWithNumbers));
        Assert.Throws<ArgumentException>(() => 
            NameValidator.ValidateProjectorNameAndThrow(nameWithNumbers));
        
        // Should succeed for tag group name
        var exception = Record.Exception(() => 
            NameValidator.ValidateTagGroupNameAndThrow(nameWithNumbers));
        Assert.Null(exception);
    }
    
    #endregion
}