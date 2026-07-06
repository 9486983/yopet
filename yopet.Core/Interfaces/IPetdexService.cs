using yopet.Core.Models;

namespace yopet.Core.Interfaces;

/// <summary>Petdex 宠物加载服务</summary>
public interface IPetdexService
{
    List<string> GetInstalledPetIds();
    PetdexManifest? LoadManifest(string petId);
    string? GetSpritesheetPath(string petId);
    PetDefinition? ToPetDefinition(string petId);
}
