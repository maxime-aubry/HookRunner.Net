namespace HookRunner.Infrastructure;

/// <summary>Constantes du protocole privé entre le superviseur et l'enfant consommateur.</summary>
public static class ChildHookProtocol
{
    /// <summary>Marqueur de commande distinguant l'entrée enfant de la commande utilisateur.</summary>
    public const string Command = "--hookrunner-child";
    /// <summary>Version courante du format de résultat échangé avec l'enfant.</summary>
    public const int Version = 1;
}
