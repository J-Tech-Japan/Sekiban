using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

public class TagValidatorTests
{
    #region Valid Tags Tests
    [Theory]
    [InlineData("Group:Content")]
    [InlineData("MyGroup:MyContent")]
    [InlineData("Group-Name:Content-Value")]
    [InlineData("Group_Name:Content_Value")]
    [InlineData("Group.Name:Content.Value")]
    [InlineData("abc:def")]
    [InlineData("ABC:DEF")]
    [InlineData("Group-With_Multiple.Separators:Content-With_Multiple.Separators")]
    public void ValidateTag_ValidTags_ReturnsNoErrors(string tagString)
    {
        // Arrange
        var tag = new TestTag(tagString);

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Empty(errors);
    }
    #endregion

    #region Edge Cases
    [Theory]
    [InlineData("a:b")] // Single character group and content
    [InlineData("A:B")]
    [InlineData("aaa...aaa:bbb---bbb")]
    [InlineData("___:___")]
    [InlineData("---:---")]
    public void ValidateTag_EdgeCases_Valid(string tagString)
    {
        // Arrange
        var tag = new TestTag(tagString);

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Empty(errors);
    }
    #endregion

    // Test helper class
    private class TestTag : ITag
    {
        private readonly string _tagString;

        public TestTag(string tagString) => _tagString = tagString;

        public bool IsConsistencyTag() => true;
        public string GetTagGroup()
        {
            if (!_tagString.Contains(':'))
                return _tagString;
            var parts = _tagString.Split(':', 2);
            return parts[0];
        }
        public string GetTagContent()
        {
            if (!_tagString.Contains(':'))
                return "";
            var parts = _tagString.Split(':', 2);
            return parts.Length > 1 ? parts[1] : "";
        }
        public string GetTag() => _tagString;
    }

    #region Invalid Format Tests
    [Fact]
    public void ValidateTag_MissingDelimiter_ReturnsFormatError()
    {
        // Arrange
        var tag = new TestTag("NoDelimiter");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        // "NoDelimiter" becomes "NoDelimiter:" when processed by TestTag, so expect empty content error
        Assert.Single(errors);
        Assert.Equal(TagValidationErrorType.EmptyContent, errors[0].ErrorType);
    }

    [Fact]
    public void ValidateTag_EmptyString_ReturnsMultipleErrors()
    {
        // Arrange
        var tag = new TestTag("");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        // Empty string becomes ":" when processed by TestTag, so expect empty group and empty content errors
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.EmptyGroup);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.EmptyContent);
    }

    [Fact]
    public void ValidateTag_WhitespaceOnly_ReturnsErrors()
    {
        // Arrange
        var tag = new TestTag("   ");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        // Whitespace becomes "   :" when processed by TestTag
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.InvalidCharactersInGroup);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.EmptyContent);
    }
    #endregion

    #region Empty Group/Content Tests
    [Fact]
    public void ValidateTag_EmptyGroup_ReturnsEmptyGroupError()
    {
        // Arrange
        var tag = new TestTag(":Content");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Single(errors);
        Assert.Equal(TagValidationErrorType.EmptyGroup, errors[0].ErrorType);
    }

    [Fact]
    public void ValidateTag_EmptyContent_ReturnsEmptyContentError()
    {
        // Arrange
        var tag = new TestTag("Group:");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Single(errors);
        Assert.Equal(TagValidationErrorType.EmptyContent, errors[0].ErrorType);
    }

    [Fact]
    public void ValidateTag_BothEmpty_ReturnsMultipleErrors()
    {
        // Arrange
        var tag = new TestTag(":");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.EmptyGroup);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.EmptyContent);
    }
    #endregion

    #region Invalid Characters Tests
    [Theory]
    [InlineData("Group@:Content", TagValidationErrorType.InvalidCharactersInGroup)]
    [InlineData("Group#:Content", TagValidationErrorType.InvalidCharactersInGroup)]
    [InlineData("Group with spaces:Content", TagValidationErrorType.InvalidCharactersInGroup)]
    [InlineData("Group[]:Content", TagValidationErrorType.InvalidCharactersInGroup)]
    [InlineData("グループ:Content", TagValidationErrorType.InvalidCharactersInGroup)]
    public void ValidateTag_InvalidCharactersInGroup_ReturnsError(
        string tagString,
        TagValidationErrorType expectedError)
    {
        // Arrange
        var tag = new TestTag(tagString);

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Single(errors);
        Assert.Equal(expectedError, errors[0].ErrorType);
        Assert.Contains("invalid characters", errors[0].Message);
    }

    [Theory]
    [InlineData("Group:Content@", TagValidationErrorType.InvalidCharactersInContent)]
    [InlineData("Group:Content#", TagValidationErrorType.InvalidCharactersInContent)]
    [InlineData("Group:Content with spaces", TagValidationErrorType.InvalidCharactersInContent)]
    [InlineData("Group:Content[]", TagValidationErrorType.InvalidCharactersInContent)]
    [InlineData("Group:コンテンツ", TagValidationErrorType.InvalidCharactersInContent)]
    [InlineData("Group:content!", TagValidationErrorType.InvalidCharactersInContent)]
    public void ValidateTag_InvalidCharactersInContent_ReturnsError(
        string tagString,
        TagValidationErrorType expectedError)
    {
        // Arrange
        var tag = new TestTag(tagString);

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Single(errors);
        Assert.Equal(expectedError, errors[0].ErrorType);
        Assert.Contains("invalid characters", errors[0].Message);
    }

    [Fact]
    public void ValidateTag_MultipleInvalidCharacters_ReturnsMultipleErrors()
    {
        // Arrange
        var tag = new TestTag("Group@#:Content!@");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.InvalidCharactersInGroup);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.InvalidCharactersInContent);
    }
    #endregion

    #region Multiple Tags Validation Tests
    [Fact]
    public void ValidateTags_AllValid_ReturnsNoErrors()
    {
        // Arrange
        var tags = new[]
        {
            new TestTag("GroupA:ContentA"),
            new TestTag("GroupB:ContentB"),
            new TestTag("Group-C:Content_C")
        };

        // Act
        var errors = TagValidator.ValidateTags(tags);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateTags_MixedValidAndInvalid_ReturnsOnlyErrors()
    {
        // Arrange
        var tags = new[]
        {
            new TestTag("ValidGroup:ValidContent"),
            new TestTag("Invalid@Group:Content"),
            new TestTag("Group:Invalid@Content"),
            new TestTag("ValidGroupB:ValidContentB")
        };

        // Act
        var errors = TagValidator.ValidateTags(tags);

        // Assert
        Assert.Equal(2, errors.Count);
    }
    #endregion

    #region Exception Throwing Tests
    [Fact]
    public void ValidateTagAndThrow_ValidTag_DoesNotThrow()
    {
        // Arrange
        var tag = new TestTag("ValidGroup:ValidContent");

        // Act & Assert
        var exception = Record.Exception(() => TagValidator.ValidateTagAndThrow(tag));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateTagAndThrow_InvalidTag_ThrowsException()
    {
        // Arrange
        var tag = new TestTag("Invalid@Group:Content");

        // Act & Assert
        var exception = Assert.Throws<TagValidationErrorsException>(() => TagValidator.ValidateTagAndThrow(tag));

        Assert.Single(exception.Errors);
        Assert.Contains("Invalid@Group", exception.Message);
    }

    [Fact]
    public void ValidateTagsAndThrow_MultipleInvalidTags_ThrowsExceptionWithAllErrors()
    {
        // Arrange
        var tags = new[]
        {
            new TestTag("Invalid@Group:Content"),
            new TestTag("Group:Invalid#Content"),
            new TestTag(":EmptyGroup")
        };

        // Act & Assert
        var exception = Assert.Throws<TagValidationErrorsException>(() => TagValidator.ValidateTagsAndThrow(tags));

        Assert.Equal(3, exception.Errors.Count);
        Assert.Contains("3 error(s)", exception.Message);
    }
    #endregion

    #region Length Validation Tests
    [Fact]
    public void ValidateTag_GroupExceedsMaxLength_ReturnsError()
    {
        // Arrange - Create a group with 41 characters (exceeds limit of 40)
        var longGroup = new string('a', 41);
        var tag = new TestTag($"{longGroup}:Content");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Single(errors);
        Assert.Equal(TagValidationErrorType.GroupTooLong, errors[0].ErrorType);
        Assert.Contains("exceeds maximum length of 40", errors[0].Message);
        Assert.Contains("actual: 41", errors[0].Message);
    }

    [Fact]
    public void ValidateTag_GroupAtMaxLength_NoError()
    {
        // Arrange - Create a group with exactly 40 characters
        var maxGroup = new string('a', 40);
        var tag = new TestTag($"{maxGroup}:Content");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateTag_ContentExceedsMaxLength_ReturnsError()
    {
        // Arrange - Create content with 81 characters (exceeds limit of 80)
        var longContent = new string('b', 81);
        var tag = new TestTag($"Group:{longContent}");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Single(errors);
        Assert.Equal(TagValidationErrorType.ContentTooLong, errors[0].ErrorType);
        Assert.Contains("exceeds maximum length of 80", errors[0].Message);
        Assert.Contains("actual: 81", errors[0].Message);
    }

    [Fact]
    public void ValidateTag_ContentAtMaxLength_NoError()
    {
        // Arrange - Create content with exactly 80 characters
        var maxContent = new string('b', 80);
        var tag = new TestTag($"Group:{maxContent}");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateTag_BothExceedMaxLength_ReturnsBothErrors()
    {
        // Arrange - Both group and content exceed limits
        var longGroup = new string('a', 45);
        var longContent = new string('b', 85);
        var tag = new TestTag($"{longGroup}:{longContent}");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.GroupTooLong);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.ContentTooLong);
    }

    [Fact]
    public void ValidateTag_RealWorldGuid_ValidLength()
    {
        // Arrange - Real-world example with GUID (36 characters)
        var tag = new TestTag("Student:550e8400-e29b-41d4-a716-446655440000");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Empty(errors); // GUID (36 chars) is within 80 char limit
    }

    [Fact]
    public void ValidateTag_MultipleValidationFailures_IncludingLength()
    {
        // Arrange - Invalid characters AND too long
        var longInvalidGroup = new string('@', 45);
        var longInvalidContent = new string('!', 85);
        var tag = new TestTag($"{longInvalidGroup}:{longInvalidContent}");

        // Act
        var errors = TagValidator.ValidateTag(tag);

        // Assert
        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.InvalidCharactersInGroup);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.InvalidCharactersInContent);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.GroupTooLong);
        Assert.Contains(errors, e => e.ErrorType == TagValidationErrorType.ContentTooLong);
    }
    #endregion
}
