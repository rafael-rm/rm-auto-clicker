using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Abstractions;

public interface IInputEmitter
{
    InputSendResult SendClick(ClickButton button);

    void ReleasePendingButtons();
}
