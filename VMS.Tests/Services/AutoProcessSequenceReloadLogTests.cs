using System.Collections.Generic;
using VMS.PLC.Models.Sequence;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// VM-07 — AUTO RUN 시작 때 시퀀스 파일을 다시 읽어도, 내용이 같으면
    /// "시퀀스를 다시 불러왔습니다." 를 남기지 않는다 (종전에는 새 객체라 늘 남았다).
    /// </summary>
    public class AutoProcessSequenceReloadLogTests
    {
        private static SequenceConfig Make(int delayMs) => new()
        {
            Id = "seq-1",
            Name = "Main",
            Nodes = new List<SequenceNodeConfig>
            {
                new() { Id = "s", NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = "d" },
                new() { Id = "d", NodeType = SequenceNodeType.Delay, Name = "Delay", DelayMs = delayMs, NextNodeId = "e" },
                new() { Id = "e", NodeType = SequenceNodeType.End, Name = "End" }
            }
        };

        [Fact]
        public void SameContent_DifferentInstances_NotChanged()
        {
            var a = AutoProcessService.SequenceSnapshot(Make(100));
            var b = AutoProcessService.SequenceSnapshot(Make(100));
            Assert.False(AutoProcessService.IsSequenceChanged(a, b));
        }

        [Fact]
        public void EditedContent_Changed()
        {
            var a = AutoProcessService.SequenceSnapshot(Make(100));
            var b = AutoProcessService.SequenceSnapshot(Make(250));
            Assert.True(AutoProcessService.IsSequenceChanged(a, b));
        }

        [Fact]
        public void NoPrevious_NotChanged()
        {
            var b = AutoProcessService.SequenceSnapshot(Make(100));
            Assert.False(AutoProcessService.IsSequenceChanged(null, b));
        }
    }
}
