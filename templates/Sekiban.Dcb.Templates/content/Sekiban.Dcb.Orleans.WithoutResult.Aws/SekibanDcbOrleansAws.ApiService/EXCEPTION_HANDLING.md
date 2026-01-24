# Exception Handling in DcbOrleans.WithoutResult.ApiService

## 概要

このAPIサービスでは、グローバル例外ハンドラーを使用して、エンドポイントから try-catch ブロックを削除し、例外の種類に応じて適切な HTTP レスポンスを返します。

## アーキテクチャ

### 1. カスタム例外クラス (`Exceptions/DomainExceptions.cs`)

```csharp
// ベース例外
public abstract class DomainException : Exception

// 具体的な例外タイプ
- NotFoundException        → 404 Not Found
- ValidationException      → 400 Bad Request (with validation errors)
- ConflictException        → 409 Conflict
- UnauthorizedException    → 401 Unauthorized
- ApplicationException     → 400 Bad Request
- その他の例外             → 500 Internal Server Error
```

### 2. グローバル例外ハンドラー (`Exceptions/GlobalExceptionHandler.cs`)

`IExceptionHandler` を実装し、すべての例外を捕捉して適切な `ProblemDetails` レスポンスに変換します。

### 3. 登録 (`Program.cs`)

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
```

## 使用パターン

### パターン1: 単純なコマンド実行

**Before:**
```csharp
async ([FromBody] CreateStudent command, [FromServices] ISekibanExecutorWithoutResult executor) =>
{
    try
    {
        var execution = await executor.ExecuteAsync(command);
        return Results.Ok(new { ... });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}
```

**After:**
```csharp
async ([FromBody] CreateStudent command, [FromServices] ISekibanExecutorWithoutResult executor) =>
{
    var execution = await executor.ExecuteAsync(command);
    return Results.Ok(new { ... });
}
```

### パターン2: リソースが見つからない場合

**Before:**
```csharp
async (Guid studentId, [FromServices] ISekibanExecutorWithoutResult executor) =>
{
    try
    {
        var tag = new StudentTag(studentId);
        var state = await executor.GetTagStateAsync(...);
        return Results.Ok(new { ... });
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = $"Student {studentId} not found" });
    }
}
```

**After:**
```csharp
async (Guid studentId, [FromServices] ISekibanExecutorWithoutResult executor) =>
{
    var tag = new StudentTag(studentId);
    TagState state;
    try
    {
        state = await executor.GetTagStateAsync(...);
    }
    catch
    {
        throw new NotFoundException("Student", studentId);
    }

    return Results.Ok(new { ... });
}
```

### パターン3: バリデーションエラー

```csharp
// ドメインロジックで検証失敗時
if (classroom.IsFull)
{
    throw new ValidationException("ClassRoom is full");
}

// 複数のエラーがある場合
var errors = new Dictionary<string, string[]>
{
    ["classRoomId"] = new[] { "ClassRoom is full" },
    ["studentId"] = new[] { "Student already enrolled" }
};
throw new ValidationException("Enrollment validation failed", errors);
```

### パターン4: 既存リソースの競合

```csharp
var exists = await context.TagExistsAsync(tag).UnwrapBox();
if (exists)
{
    throw new ConflictException($"Student with ID {command.StudentId} already exists");
}
```

## レスポンス形式

すべてのエラーは RFC 7807 準拠の `ProblemDetails` 形式で返されます：

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Resource Not Found",
  "status": 404,
  "detail": "Student with ID '123e4567-e89b-12d3-a456-426614174000' was not found.",
  "resourceType": "Student",
  "resourceId": "123e4567-e89b-12d3-a456-426614174000"
}
```

## 実装済みエンドポイント

以下のエンドポイントは既に例外ハンドラーを使用するよう更新されています：

- ✅ POST `/api/students` - CreateStudent
- ✅ GET `/api/students` - GetStudentList
- ✅ GET `/api/students/{studentId}` - GetStudent (with NotFoundException)
- ✅ POST `/api/classrooms` - CreateClassRoom

## 残りのエンドポイント

以下のエンドポイントは、必要に応じて同じパターンで更新できます：

- GET `/api/classrooms` - GetClassRoomList
- GET `/api/classrooms/{classRoomId}` - GetClassRoom
- POST `/api/enrollments/add` - EnrollStudent
- POST `/api/enrollments/drop` - DropStudent
- GET `/api/weatherforecast` - GetWeatherForecast
- POST `/api/inputweatherforecast` - CreateWeatherForecast
- 等々...

## ログレベル

`GlobalExceptionHandler` は例外をステータスコードに応じて自動的にログに記録します：

- 500番台のエラー → `LogLevel.Error`
- 400番台のエラー → `LogLevel.Warning`
- その他 → `LogLevel.Information`

## テスト方法

```bash
# 成功例
curl -X POST http://localhost:5141/api/students \
  -H "Content-Type: application/json" \
  -d '{"studentId":"123e4567-e89b-12d3-a456-426614174000","name":"Test Student","maxClassCount":5}'

# Not Found例
curl http://localhost:5141/api/students/00000000-0000-0000-0000-000000000000

# Conflict例（既存のstudentIdで再度作成）
curl -X POST http://localhost:5141/api/students \
  -H "Content-Type: application/json" \
  -d '{"studentId":"123e4567-e89b-12d3-a456-426614174000","name":"Duplicate","maxClassCount":3}'
```
