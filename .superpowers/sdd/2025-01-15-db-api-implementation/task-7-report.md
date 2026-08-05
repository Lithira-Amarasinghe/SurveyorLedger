# Task 7: PasswordService & AuthService - Implementation Report

**Date:** 2026-08-05  
**Status:** COMPLETED

## Files Created

1. **src/SurveyorLedger.API/Services/PasswordService.cs**
   - Implements `IPasswordService` interface
   - BCrypt password hashing with cost factor 12
   - Methods: `HashPassword()`, `VerifyPassword()`

2. **src/SurveyorLedger.API/Services/AuthService.cs**
   - Implements `IAuthService` interface
   - Core auth business logic: registration, login, OTP verification
   - Methods:
     - `RegisterAsync()` - Create user, generate OTP, send verification email
     - `LoginAsync()` - Verify credentials and email verification status
     - `VerifyOtpAsync()` - Validate OTP and mark email as verified
     - `GetUserByEmailAsync()` - Retrieve active user by email

## Key Implementation Details

### PasswordService
- Uses BCrypt.Net-Next library (already in dependencies)
- BCrypt cost factor = 12 for security
- Graceful error handling in `VerifyPassword()` for malformed hashes

### AuthService
- **Dependencies:** ApplicationDbContext, IPasswordService, ITokenService, IEmailService, IConfiguration, ILogger
- **Registration flow:**
  1. Check email uniqueness (using IgnoreQueryFilters to bypass soft-delete)
  2. Create user with hashed password, unverified email
  3. Generate 6-digit OTP and hash it before storage
  4. Create EmailVerification record with OTP hash
  5. Send OTP email (error logged but not thrown)
  6. Generate access/refresh tokens for unverified user
  
- **Login flow:**
  1. Find active user by email
  2. Verify password against stored hash
  3. Check email is verified
  4. Generate tokens
  
- **OTP verification flow:**
  1. Find pending EmailVerification record
  2. Check OTP not expired
  3. Check max attempts (3) not exceeded
  4. Verify OTP against hash
  5. Mark verification complete and user email verified

### Security Measures
- Passwords hashed with BCrypt (cost 12)
- OTP codes hashed before database storage
- OTP expiration: 10 minutes (configurable)
- OTP max attempts: 3 (configurable)
- Email verification required for login
- Soft-delete query filters bypass for registration (IgnoreQueryFilters)

### Configuration Keys Used
- `OTP:ExpirationMinutes` (default: 10)
- `OTP:MaxAttempts` (default: 3)
- JwtSettings (delegated to TokenService)

## Program.cs Updates

Added service registrations in Program.cs:
```csharp
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
```

EmailService and EmailClient registration already existed.

## Build Results

**Status:** ✅ SUCCESS

```
Build output:
  SurveyorLedger.Core -> ...bin/Debug/net9.0/SurveyorLedger.Core.dll
  SurveyorLedger.Data -> ...bin/Debug/net9.0/SurveyorLedger.Data.dll
  SurveyorLedger.API -> ...bin/Debug/net9.0/SurveyorLedger.API.dll

Build succeeded with 2 warnings (0 errors)
Time Elapsed: 00:00:04.33
```

**Warnings:** Pre-existing Casbin.NET version mismatch (1.28.0 vs 2.0.0 resolved) - unrelated to this task.

## Issues Fixed

Fixed pre-existing CasbinService compilation errors:
- Updated to use `DefaultModel.CreateFromText()` (Casbin.NET 2.0.0 API)
- Fixed async method signatures (`EnforceAsync`, `AddRoleForUserAsync`, `RemoveRoleForUserAsync`)
- Changed from `async Task` to `Task` with `Task.FromResult()` / `Task.CompletedTask`

## Design Notes

**Ponytail observations:**
- OTP stored as hash (not plaintext) for security → line 101, 108
- Max attempts tracking prevents brute force → line 193-194
- Email send errors caught but not rethrown → allows user to proceed to OTP endpoint, resend later
- Tokens generated for unverified users → allows access only to `/verify-otp` endpoint (enforced by controller)
- Uses config values for OTP settings → easily tunable without code changes

## Next Steps (Task 8-9)

- Task 8: EmailService already exists (Azure Communication Services)
- Task 9: TokenService already exists (JWT generation + validation)
- Task 10+: Controllers will use these services for endpoints

## Testing Status

**Compile-time only.** Full integration/unit testing in Task 16 with Testcontainers.

Files are syntactically correct and follow project patterns:
- Nullable reference types enabled
- Async/await throughout
- Dependency injection
- Custom exception handling with AppException
- Logging on key operations
