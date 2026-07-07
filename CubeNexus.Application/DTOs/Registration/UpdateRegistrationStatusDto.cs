namespace CubeNexus.Application.DTOs.Registration;

public class UpdateRegistrationStatusDto
{
    public string Status { get; set; } = string.Empty; // CONFIRMED, CANCELLED, PENDING
}
