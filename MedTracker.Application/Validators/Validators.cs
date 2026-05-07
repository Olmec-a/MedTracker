using FluentValidation;
using MedTracker.Application.DTOs;

namespace MedTracker.Application.Validators;

public class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email format is invalid.")
            .MaximumLength(254).WithMessage("Email must not exceed 254 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(100)
            .Matches(@"[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain a digit.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(200);

        RuleFor(x => x.Age)
            .InclusiveBetween(10, 120).WithMessage("Age must be between 10 and 120.");
    }
}

public class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email format is invalid.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

public class ChangePasswordDtoValidator : AbstractValidator<ChangePasswordDto>
{
    public ChangePasswordDtoValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8).WithMessage("New password must be at least 8 characters.")
            .MaximumLength(100)
            .Matches(@"[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain a digit.")
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must differ from current.");
    }
}

public class ConfirmEmailDtoValidator : AbstractValidator<ConfirmEmailDto>
{
    public ConfirmEmailDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
    }
}

public class ResendConfirmationDtoValidator : AbstractValidator<ResendConfirmationDto>
{
    public ResendConfirmationDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class RequestPasswordResetDtoValidator : AbstractValidator<RequestPasswordResetDto>
{
    public RequestPasswordResetDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class ResetPasswordDtoValidator : AbstractValidator<ResetPasswordDto>
{
    public ResetPasswordDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(100)
            .Matches(@"[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain a digit.");
    }
}

public class UpdateProfileDtoValidator : AbstractValidator<UpdateProfileDto>
{
    public UpdateProfileDtoValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(200);

        RuleFor(x => x.Age)
            .InclusiveBetween(10, 120).WithMessage("Age must be between 10 and 120.");
    }
}

public class AssignMedicationDtoValidator : AbstractValidator<AssignMedicationDto>
{
    public AssignMedicationDtoValidator()
    {
        RuleFor(x => x.MedicationId).NotEmpty().WithMessage("Medication ID is required.");
        RuleFor(x => x.StartDate).NotEmpty().WithMessage("Start date is required.");
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("End date must be after start date.");
    }
}

public class AssignSupplementDtoValidator : AbstractValidator<AssignSupplementDto>
{
    public AssignSupplementDtoValidator()
    {
        RuleFor(x => x.SupplementId).NotEmpty().WithMessage("Supplement ID is required.");
        RuleFor(x => x.StartDate).NotEmpty().WithMessage("Start date is required.");
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("End date must be after start date.");
    }
}

public class CreateSideEffectLogDtoValidator : AbstractValidator<CreateSideEffectLogDto>
{
    public CreateSideEffectLogDtoValidator()
    {
        RuleFor(x => x.SideEffectId).NotEmpty().WithMessage("Side effect ID is required.");
        RuleFor(x => x.Date).NotEmpty().WithMessage("Date is required.");
        RuleFor(x => x.Intensity).IsInEnum().WithMessage("Invalid intensity value.");
        RuleFor(x => x.Comment).MaximumLength(1000).When(x => x.Comment != null);
    }
}

public class CreateExternalMedicationDtoValidator : AbstractValidator<CreateExternalMedicationDto>
{
    public CreateExternalMedicationDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300).WithMessage("Name is required.");
        RuleFor(x => x.Dosage).NotEmpty().MaximumLength(100).WithMessage("Dosage is required.");
        RuleFor(x => x.Date).NotEmpty().WithMessage("Date is required.");
        RuleFor(x => x.Comment).MaximumLength(1000).When(x => x.Comment != null);
    }
}

public class CreateCycleEntryDtoValidator : AbstractValidator<CreateCycleEntryDto>
{
    public CreateCycleEntryDtoValidator()
    {
        RuleFor(x => x.StartDate).NotEmpty().WithMessage("Start date is required.");
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("End date must be after start date.");
        RuleFor(x => x.Intensity).IsInEnum().WithMessage("Invalid intensity value.");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes != null);
    }
}

public class UpdateCycleEntryDtoValidator : AbstractValidator<UpdateCycleEntryDto>
{
    public UpdateCycleEntryDtoValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.StartDate).NotEmpty().WithMessage("Start date is required.");
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("End date must be after start date.");
        RuleFor(x => x.Intensity).IsInEnum().WithMessage("Invalid intensity value.");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes != null);
    }
}