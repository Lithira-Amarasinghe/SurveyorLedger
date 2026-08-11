namespace SurveyorLedger.Core;

public enum TokenType
{
    AccessToken,
    RefreshToken,
    OTPToken
}

public enum EmailVerificationType
{
    Registration,
    PasswordReset,
    EmailVerification
}

public enum SubscriptionStatus
{
    Active,
    Canceled,
    Expired
}

public enum SubscriptionTier
{
    Free,
    Paid
}

public enum DocumentCategory
{
    SurveyPlan,
    LegalDocument,
    Photo,
    Other
}

public enum DocumentVisibility
{
    Internal,
    ClientVisible
}
