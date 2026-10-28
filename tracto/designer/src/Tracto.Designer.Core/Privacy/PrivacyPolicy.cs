namespace Tracto.Designer.Core.Privacy;

[Flags]
public enum PrivacyPolicy
{
    None            = 0,
    RedactPasswords = 1 << 0,
    RedactEmail     = 1 << 1,
    RedactPhone     = 1 << 2,
    RedactSsn       = 1 << 3,
    Default         = RedactPasswords | RedactEmail
}
