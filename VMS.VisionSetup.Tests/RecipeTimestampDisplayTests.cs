using System;
using System.Text.Json;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-04: 레시피 수정 시각은 UTC 로 저장하고 화면에는 현지 시각으로 보인다.
    /// 종전에는 UTC 값을 변환 없이 보여 9시간 이르게 표시됐다.
    /// </summary>
    public class RecipeTimestampDisplayTests
    {
        [Fact]
        public void RecipeInfo_ShowsLocalTime_ForUtcValue()
        {
            var utc = new DateTime(2026, 11, 13, 7, 54, 0, DateTimeKind.Utc);
            var info = new RecipeInfo { ModifiedAt = utc };

            Assert.Equal(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), info.ModifiedAtDisplay);
        }

        [Fact]
        public void Recipe_ShowsLocalTime_AfterJsonRoundTrip()
        {
            var utc = new DateTime(2026, 11, 13, 7, 54, 0, DateTimeKind.Utc);
            var recipe = new Recipe { Name = "R", ModifiedAt = utc };

            var json = JsonSerializer.Serialize(recipe, RecipeJson.Options);
            var back = JsonSerializer.Deserialize<Recipe>(json, RecipeJson.Options)!;

            Assert.Equal(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), back.ModifiedAtDisplay);
        }

        [Fact]
        public void Recipe_LocalValue_IsNotShiftedAgain()
        {
            // 예전 편집기 [Save] 가 DateTime.Now 로 저장한 파일 — 다시 옮기지 않아야 한다.
            var local = new DateTime(2026, 11, 13, 16, 54, 0, DateTimeKind.Local);
            var recipe = new Recipe { Name = "R", ModifiedAt = local };

            var json = JsonSerializer.Serialize(recipe, RecipeJson.Options);
            var back = JsonSerializer.Deserialize<Recipe>(json, RecipeJson.Options)!;

            Assert.Equal("2026-11-13 16:54:00", back.ModifiedAtDisplay);
        }
    }
}
