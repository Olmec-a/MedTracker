using FluentValidation;
using MedTracker.Application.DTOs;

namespace MedTracker.Application.Validators;

public class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(x => x.Login)
            .NotEmpty().WithMessage("Login is required.")
            .MinimumLength(3).WithMessage("Login must be at least 3 characters.")
            .MaximumLength(50).WithMessage("Login must not exceed 50 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.")
            .MaximumLength(100);

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
        RuleFor(x => x.Login).NotEmpty().WithMessage("Login is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
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