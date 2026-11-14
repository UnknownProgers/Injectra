namespace Injectra.Internal.Activation;

internal interface IActivation
{
	object Create(Container container);
}
