using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.VisionSetup.Interfaces;
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

        [ObservableProperty]
        private string _userInput = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isModelLoaded;

        [ObservableProperty]
        private string _modelName = "gemma3:4b";

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
            MainViewModel mainViewModel)
        {
            _chatService = chatService;
            _toolGenerator = toolGenerator;
            _mainViewModel = mainViewModel;

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

            // 캔버스 컨텍스트 설정
            _chatService.SetCanvasContext(_toolGenerator.BuildCanvasContext(_mainViewModel));

            try
            {
                // 스트리밍 토큰은 UI에 표시하지 않고 내부에서만 수집
                string response = await _chatService.SendMessageAsync(message, onToken: null, _cts.Token);

                _lastJsonResponse = response;

                // JSON을 파싱하여 사람이 읽을 수 있는 미리보기로 변환
                var action = _toolGenerator.ParseActionJson(response);
                if (action != null)
                {
                    string preview = _toolGenerator.BuildPreviewText(action);
                    botMessage.Content = preview;
                    StatusMessage = "레시피 생성 완료. 'Apply Recipe'를 클릭하여 적용하세요.";
                }
                else
                {
                    // 파싱 실패 시 원본 응답 표시
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
    }
}
