// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;
public sealed partial class StoreXboxAccountProvider:IStoreAchievementsProvider
{
    async Task<IReadOnlyList<StoreAchievement>> IStoreAchievementsProvider.ReadAchievementsAsync(StoreLibraryGame game,string language,CancellationToken token)
        =>StoreAchievementNormalization.Xbox((await ReadXboxAchievementsAsync(game.Key.Id,game.AccountRevision,language,token)).Achievements);
}
