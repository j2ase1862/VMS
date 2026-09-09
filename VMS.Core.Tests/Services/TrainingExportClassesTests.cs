using System;
using System.IO;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 학습에 실제로 들어간 클래스를 내보내기 폴더에서 되읽는다.
    ///
    /// <para>
    /// 이 값이 틀리면 이름이 밀린 모델이 레지스트리에 등록된다. 서버가 ONNX 안의 이름과
    /// 대조할 수 있는 규약에서는 400 으로 막히지만, 그러지 못하는 규약에서는 조용히 통과하고
    /// 라인의 검사 결과 이름이 통째로 틀린다. 순서까지 지켜야 한다 — 이름은 인덱스와 짝이다.
    /// </para>
    /// </summary>
    public class TrainingExportClassesTests : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "vms-export-" + Guid.NewGuid().ToString("N"));

        private string Dir(params string[] parts)
        {
            var path = Path.Combine(new[] { _root }.Concat2(parts));
            Directory.CreateDirectory(path);
            return path;
        }

        private void Write(string relative, string content)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        [Fact]
        public void Reads_indexed_names_from_data_yaml()
        {
            // MLOps 의 DatasetExportWriter 가 쓰는 모양
            Write("data.yaml", "path: .\ntrain: images/train\nval: images/val\nnames:\n  0: good\n  1: defect\n");

            Assert.Equal(new[] { "good", "defect" }, TrainingExportClasses.Read(_root));
        }

        /// <summary>번호가 뒤섞여 있어도 번호 순으로 세운다 — 순서가 곧 뜻이다.</summary>
        [Fact]
        public void Orders_data_yaml_names_by_index()
        {
            Write("data.yaml", "names:\n  1: defect\n  0: good\n  2: scratch\n");

            Assert.Equal(new[] { "good", "defect", "scratch" }, TrainingExportClasses.Read(_root));
        }

        [Fact]
        public void Reads_list_style_data_yaml()
        {
            Write("data.yaml", "train: images/train\nnames:\n  - good\n  - 'defect'\n  - \"scratch\"\n");

            Assert.Equal(new[] { "good", "defect", "scratch" }, TrainingExportClasses.Read(_root));
        }

        [Fact]
        public void Reads_inline_data_yaml()
        {
            Write("data.yaml", "nc: 2\nnames: [good, defect]\n");

            Assert.Equal(new[] { "good", "defect" }, TrainingExportClasses.Read(_root));
        }

        /// <summary>names 블록이 끝난 뒤의 들여쓴 줄을 클래스로 삼으면 안 된다.</summary>
        [Fact]
        public void Stops_at_the_next_top_level_key()
        {
            Write("data.yaml", "names:\n  0: good\n  1: defect\nextra:\n  0: 딴것\n");

            Assert.Equal(new[] { "good", "defect" }, TrainingExportClasses.Read(_root));
        }

        /// <summary>coco 는 카테고리 id 순이다 (MLOps 내보내기는 1 부터 매긴다).</summary>
        [Fact]
        public void Reads_coco_categories_in_id_order()
        {
            Write(Path.Combine("annotations", "instances_train.json"),
                """{"images":[],"annotations":[],"categories":[{"id":2,"name":"dent"},{"id":1,"name":"scratch"}]}""");

            Assert.Equal(new[] { "scratch", "dent" }, TrainingExportClasses.Read(_root));
        }

        [Fact]
        public void Reads_imagefolder_class_directories()
        {
            Dir("train", "ng");
            Dir("train", "ok");

            Assert.Equal(new[] { "ng", "ok" }, TrainingExportClasses.Read(_root));
        }

        /// <summary>못 읽으면 빈 목록. 부르는 쪽이 원래 쓰던 값으로 되돌아간다.</summary>
        [Fact]
        public void Returns_empty_when_nothing_is_readable()
        {
            Directory.CreateDirectory(_root);

            Assert.Empty(TrainingExportClasses.Read(_root));
            Assert.Empty(TrainingExportClasses.Read(Path.Combine(_root, "없는폴더")));
            Assert.Empty(TrainingExportClasses.Read(""));
            Assert.Empty(TrainingExportClasses.Read(null));
        }

        /// <summary>깨진 파일에 앱이 멈추면 안 된다 — 클래스를 못 읽었을 뿐이다.</summary>
        [Fact]
        public void Survives_a_broken_coco_file()
        {
            Write(Path.Combine("annotations", "instances_train.json"), "{ 이건 json 이 아니다");

            Assert.Empty(TrainingExportClasses.Read(_root));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        }
    }

    internal static class PathParts
    {
        /// <summary>Path.Combine 이 params 배열 앞에 뿌리를 붙이도록 잇는다.</summary>
        public static string[] Concat2(this string[] head, string[] tail)
        {
            var all = new string[head.Length + tail.Length];
            head.CopyTo(all, 0);
            tail.CopyTo(all, head.Length);
            return all;
        }
    }
}
