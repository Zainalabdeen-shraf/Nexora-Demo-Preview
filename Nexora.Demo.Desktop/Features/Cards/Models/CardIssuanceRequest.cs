namespace Nexora.Demo.Desktop.Features.Cards.Models;

public sealed record CardIssuanceRequest(
    DemoAccountSystem TargetSystem,
    int Quantity,
    DemoCardPackage Package,
    int CardNumberLength,
    string CardNumberPrefix,
    string CardNumberSuffix,
    CardNumberCharacterSet CardNumberCharacterSet,
    CardNumberGenerationMode NumberGenerationMode,
    string SequenceStartValue,
    int UsernameLength,
    CredentialCharacterSet UsernameCharacterSet,
    PasswordGenerationMode PasswordMode,
    int PasswordLength,
    CredentialCharacterSet PasswordCharacterSet,
    string RouterProfile,
    string PointOfSale,
    string Comment,
    string ManagerAccount = "admin",
    int SharedUsers = 1,
    string BatchName = "");
