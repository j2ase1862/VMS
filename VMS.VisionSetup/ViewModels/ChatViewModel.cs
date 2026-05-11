using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.ViewModels
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _content = string.Empty;

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            }
        }

        public bool IsUser { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ChatViewModel : ObservableObject
    {
        private readonly ISLMChatService _chatService;
        private readonly SLMToolGeneratorService _toolGenerator;
        private readonly MainViewModel _mainViewModel;
        private readonly IImageAnalysisService? _imageAnalysisService;
        private const int MaxAnalysisRounds = 1;

        [ObservableProperty]
        private string _userInput = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isModelLoaded;

        [ObservableProperty]
        private string _modelName = "qwen2.5:7b-instruct";

        [ObservableProperty]
        private string _statusMessage = "모델 이름을 입력하고 Connect를 클릭하세요.";

        [ObservableProperty]
        private bool _isOperatorMode;

        private string? _lastJsonResponse;
        private string? _lastUserRequest;
        private CancellationTokenSource? _cts;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public Action? CloseAction { get; set; }

        public ChatViewModel(
            ISLMChatService chatService,
            SLMToolGeneratorService toolGenerator,
            MainViewModel mainViewModel,
            IImageAnalysisService? imageAnalysisService = null)
        {
            _chatService = chatService;
            _toolGenerator = toolGenerator;
            _mainViewModel = mainViewModel;
            _imageAnalysisService = imageAnalysisService;

            // 자동 연결 시도
            _ = ConnectModel();
        }

        partial void OnIsBusyChanged(bool value) => NotifyAllCommands();

        partial void OnIsModelLoadedChanged(bool value) => NotifyAllCommands();

        partial void OnUserInputChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();

        partial void OnModelNameChanged(string value) => ConnectModelCommand.NotifyCanExecuteChanged();

        private void NotifyAllCommands()
        {
            SendMessageCommand.NotifyCanExecuteChanged();
            ConnectModelCommand.NotifyCanExecuteChanged();
            ApplyRecipeCommand.NotifyCanExecuteChanged();
            ResetSessionCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsOperatorModeChanged(bool value)
        {
            _chatService.SetMode(value);
            if (IsModelLoaded)
            {
                ResetSession();
                string modeName = value ? "Operator" : "Expert";
                Messages.Add(new ChatMessage
                {
                    Content = $"{modeName} 모드로 전환되었습니다.",
                    IsUser = false
                });
                StatusMessage = $"{modeName} 모드 활성화";
            }
        }

        [RelayCommand(CanExecute = nameof(CanConnect))]
        private async System.Threading.Tasks.Task ConnectModel()
        {
            IsBusy = true;
            StatusMessage = "Ollama 연결 중...";

            try
            {
                await _chatService.LoadModelAsync(ModelName);
                IsModelLoaded = true;
                StatusMessage = $"연결 완료 (모델: {ModelName})";

                Messages.Add(new ChatMessage
                {
                    Content = $"Ollama '{ModelName}' 모델에 연결되었습니다.\n비전 검사 요구사항을 설명하면 레시피를 생성해 드립니다.",
                    IsUser = false
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"연결 실패: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanConnect() => !string.IsNullOrWhiteSpace(ModelName) && !IsBusy;

        [RelayCommand(CanExecute = nameof(CanSendMessage))]
        private async System.Threading.Tasks.Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(UserInput))
                return;

            string message = UserInput.Trim();
            _lastUserRequest = message;
            UserInput = string.Empty;

            Messages.Add(new ChatMessage { Content = message, IsUser = true });

            // 생성 중 메시지 (스트리밍 JSON은 사용자에게 보여주지 않음)
            var botMessage = new ChatMessage { Content = "레시피를 생성하고 있습니다...", IsUser = false };
            Messages.Add(botMessage);

            IsBusy = true;
            StatusMessage = "AI가 레시피를 생성 중...";
            _cts = new CancellationTokenSource();

            // 캔버스 + 실행 피드백 컨텍스트 설정 (Phase 5)
            _chatService.SetCanvasContext(BuildSessionContext());

            try
            {
                string response = await _chatService.SendMessageAsync(message, onToken: null, _cts.Token);
                var action = _toolGenerator.ParseActionJson(response);

                // Phase 3: 2단계 추론 — AnalysisRequests가 있으면 분석 실행 후 재호출.
                int round = 0;
                while (action != null && action.AnalysisRequests != null
                       && action.AnalysisRequests.Count > 0
                       && round < MaxAnalysisRounds)
                {
                    round++;
                    botMessage.Content = action.Reason is { Length: > 0 } r
                        ? $"이미지 분석 중... ({r})"
                        : "이미지 분석 중...";
                    StatusMessage = "이미지 분석 중...";

                    string? analysisContext = TryBuildAnalysisContext(action.AnalysisRequests, action.AnalysisRoi);
                    if (analysisContext == null)
                    {
                        botMessage.Content = "이미지가 로드되지 않았거나 분석 서비스가 없습니다. "
                            + "이미지를 먼저 열거나 다시 요청해 주세요.";
                        StatusMessage = "분석 실패";
                        return;
                    }

                    response = await _chatService.SendMessageAsync(analysisContext, onToken: null, _cts.Token);
                    action = _toolGenerator.ParseActionJson(response);
                }

                _lastJsonResponse = response;

                if (action != null)
                {
                    if (action.AnalysisRequests != null && action.AnalysisRequests.Count > 0)
                    {
                        // LLM이 분석 결과 받고도 또 분석 요청 → 안전상 거부
                        botMessage.Content = "분석 결과를 활용하지 못했습니다. 요청을 더 구체적으로 적어 주세요.";
                        StatusMessage = "분석 루프 중단";
                        return;
                    }

                    string preview = _toolGenerator.BuildPreviewText(action);
                    botMessage.Content = preview;
                    StatusMessage = "레시피 생성 완료. 'Apply Recipe'를 클릭하여 적용하세요.";
                }
                else
                {
                    botMessage.Content = $"JSON 파싱에 실패했습니다.\n\n원본 응답:\n{response}";
                    StatusMessage = "응답을 파싱할 수 없습니다.";
                }
            }
            catch (OperationCanceledException)
            {
                botMessage.Content = "요청이 취소되었습니다.";
                StatusMessage = "요청이 취소되었습니다.";
            }
            catch (Exception ex)
            {
                botMessage.Content = $"오류가 발생했습니다: {ex.Message}";
                StatusMessage = $"오류: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                _cts = null;
            }
        }

        private bool CanSendMessage() => IsModelLoaded && !IsBusy;

        [RelayCommand(CanExecute = nameof(CanApplyRecipe))]
        private void ApplyRecipe()
        {
            if (string.IsNullOrEmpty(_lastJsonResponse))
                return;

            IsBusy = true;
            StatusMessage = "레시피 적용 중...";

            try
            {
                var action = _toolGenerator.ParseActionJson(_lastJsonResponse);
                if (action == null)
                {
                    StatusMessage = "JSON 파싱 실패.";
                    return;
                }

                if (action.Action.Equals("Modify", StringComparison.OrdinalIgnoreCase))
                {
                    var (applied, errors) = _toolGenerator.ApplyModifications(_mainViewModel, action);

                    if (applied > 0)
                    {
                        StatusMessage = $"수정 완료: {applied}개 변경 적용됨";
                        Messages.Add(new ChatMessage
                        {
                            Content = $"{applied}개 수정 사항이 적용되었습니다.",
                            IsUser = false
                        });
                    }
                    else
                    {
                        StatusMessage = "수정 적용 실패.";
                    }

                    if (errors.Count > 0)
                    {
                        Messages.Add(new ChatMessage
                        {
                            Content = $"경고: {string.Join(", ", errors)}",
                            IsUser = false
                        });
                    }
                }
                else
                {
                    var (created, errors) = _toolGenerator.ApplyRecipe(_mainViewModel, action);

                    if (created > 0)
                    {
                        StatusMessage = $"레시피 '{action.RecipeName}' 적용 완료: {created}개 도구 생성됨";
                        Messages.Add(new ChatMessage
                        {
                            Content = $"레시피 '{action.RecipeName}'이 적용되었습니다. ({created}개 도구)",
                            IsUser = false
                        });
                    }
                    else
                    {
                        StatusMessage = "레시피 적용 실패.";
                    }

                    if (errors.Count > 0)
                    {
                        Messages.Add(new ChatMessage
                        {
                            Content = $"경고: {string.Join(", ", errors)}",
                            IsUser = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"적용 오류: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanApplyRecipe() => !string.IsNullOrEmpty(_lastJsonResponse) && !IsBusy;

        [RelayCommand(CanExecute = nameof(CanResetSession))]
        private void ResetSession()
        {
            _chatService.ResetSession();
            Messages.Clear();
            _lastJsonResponse = null;
            _lastUserRequest = null;
            StatusMessage = "세션 초기화 완료. 요청을 입력하세요.";
            Messages.Add(new ChatMessage
            {
                Content = "세션이 초기화되었습니다. 비전 검사 요구사항을 설명해 주세요.",
                IsUser = false
            });
        }

        private bool CanResetSession() => IsModelLoaded && !IsBusy;

        [RelayCommand]
        private void Close()
        {
            _cts?.Cancel();
            CloseAction?.Invoke();
        }

        /// <summary>
        /// Canvas 상태 + Execution Feedback을 한 묶음으로 prepend.
        /// LLM은 시스템 프롬프트의 정의에 따라 각 [...] 라벨을 해석.
        /// </summary>
        private string? BuildSessionContext()
        {
            var canvas = _toolGenerator.BuildCanvasContext(_mainViewModel);
            var feedback = ExecutionFeedbackBuilder.Build(_mainViewModel.ExecutionQueue);

            if (string.IsNullOrEmpty(canvas) && string.IsNullOrEmpty(feedback))
                return null;

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(canvas)) parts.Add(canvas);
            if (!string.IsNullOrEmpty(feedback)) parts.Add(feedback);
            return string.Join("\n\n", parts);
        }

        /// <summary>
        /// SLM이 요청한 분석을 현재 이미지에 대해 실행하고, LLM이 다음 턴에 읽기 좋은
        /// 텍스트 블록으로 직렬화. 실패 시 null 반환.
        /// </summary>
        private string? TryBuildAnalysisContext(
            System.Collections.Generic.List<string> requests,
            RoiHint? analysisRoi)
        {
            if (_imageAnalysisService == null) return null;

            var image = _mainViewModel.CurrentImage;
            if (image == null || image.Empty()) return null;

            // Phase 4a: AnalysisRoi 힌트가 있으면 해당 영역에 한정 분석
            OpenCvSharp.Rect? roiRect = null;
            if (analysisRoi != null
                && SLMToolGeneratorService.TryResolveRoi(analysisRoi, out var resolved, out _))
            {
                roiRect = resolved;
            }

            ImageAnalysisBundle bundle;
            try
            {
                bundle = _imageAnalysisService.AnalyzeBundle(image, requests.ToArray(), roi: roiRect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Analysis failed: {ex.Message}");
                return null;
            }

            // LLM-친화 직렬화: 빈 필드는 제외, 들여쓰기 적용.
            string json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });

            return "[Image Analysis]\n" + json
                + "\n\n[Original Request]\n" + (_lastUserRequest ?? "");
        }
    }
}
