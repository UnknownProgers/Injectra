namespace Injectra.Internal.Registration;

internal interface IRegistrationBuilder
{
	Registration Snapshot();
	void Complete();
}
