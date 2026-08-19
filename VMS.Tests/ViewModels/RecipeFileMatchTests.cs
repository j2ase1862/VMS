using System;
using System.IO;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 외부 레시피 변경 워처의 id 폴백 매칭 검증 —
    /// 현장 사고(2026-08-19): VisionSetup 이 원본과 다른 파일명(recipe_&lt;이름&gt;.json)으로
    /// 저장하면 경로 비교 필터에서 탈락해 수정이 영원히 반영되지 않았다.
    /// 파일명이 달라도 내용의 id 가 현재 레시피와 같으면 같은 레시피로 인정한다.
    /// </summary>
    public class RecipeFileMatchTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"vms_recipe_match_{Guid.NewGuid():N}");

        public RecipeFileMatchTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* 무시 */ }
        }

        private string WriteFile(string name, string json)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void MatchingId_ReturnsTrue()
        {
            var path = WriteFile("recipe_다른이름.json", """{"id":"abc-123","name":"R1"}""");
            Assert.True(MainViewModel.RecipeFileMatchesId(path, "abc-123"));
        }

        [Fact]
        public void MatchingId_CaseInsensitive()
        {
            var path = WriteFile("recipe_x.json", """{"id":"ABC-123","name":"R1"}""");
            Assert.True(MainViewModel.RecipeFileMatchesId(path, "abc-123"));
        }

        [Fact]
        public void DifferentId_ReturnsFalse()
        {
            var path = WriteFile("recipe_y.json", """{"id":"other","name":"R2"}""");
            Assert.False(MainViewModel.RecipeFileMatchesId(path, "abc-123"));
        }

        [Fact]
        public void NoCurrentId_ReturnsFalse()
        {
            var path = WriteFile("recipe_z.json", """{"id":"abc-123"}""");
            Assert.False(MainViewModel.RecipeFileMatchesId(path, null));
            Assert.False(MainViewModel.RecipeFileMatchesId(path, ""));
        }

        [Fact]
        public void CorruptOrMissingFile_ReturnsFalse()
        {
            var partial = WriteFile("recipe_partial.json", """{"id":"abc-1""");
            Assert.False(MainViewModel.RecipeFileMatchesId(partial, "abc-123"));
            Assert.False(MainViewModel.RecipeFileMatchesId(Path.Combine(_dir, "없는파일.json"), "abc-123"));
        }
    }
}
