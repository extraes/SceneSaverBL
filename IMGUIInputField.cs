namespace SceneSaverBL;

internal class IMGUIInputField
{
    const string IMGUI_CONTROL_NAME = "imguiInputField";

    string str;
    int duration;
    float startTime;
    string filter;
    UniTaskCompletionSource<string> taskCompletionSource;
    GUIStyle? style;

    public UniTask<string> OnComplete => taskCompletionSource.Task;
    public bool TimeFinished => duration != -1 && Time.realtimeSinceStartup >= startTime + duration;
    GUIStyle Style
    {
        get
        {
            if (style is null || style.WasCollected)
                style = new GUIStyle(GUI.skin.textField);
            style.fontSize = (int)Screen.dpi / 4;
            if (style.fontSize == 0)
                style.fontSize = 24;
            return style;
        }
    }

        

    public IMGUIInputField(int duration, string startStr = "", string filter = "")
    {
        startTime = Time.realtimeSinceStartup;
        this.duration = duration;
        this.str = startStr;
        this.filter = filter;
        taskCompletionSource = new();
    }

    public void OnGUI()
    {
        if (TimeFinished)
        {
            Finish();
            return;
        }

        GUI.SetNextControlName(IMGUI_CONTROL_NAME);
        // put the enterpressed check here because supposedly the GUI.TextField call will eat enter presses
        bool enterPressed = Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

        if (GUI.GetNameOfFocusedControl() == IMGUI_CONTROL_NAME && enterPressed)
        {
            Finish();
            return;
        }

        float x = Screen.width * 0.2f;
        float y = Screen.height * 0.475f;

        float width = Screen.width - 2 * x;
        float height = Screen.height - 2 * y;
        Rect rField = new(x, y, width, height);

        string stringPre = str;
        str = GUI.TextField(rField, str, 64, Style);
        if (!string.IsNullOrEmpty(filter) && stringPre != str)
        {
            StringBuilder sb = new(str.Length);
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (filter.Contains(c))
                    sb.Append(c);
                else if (filter.Contains(char.ToUpper(c)))
                    sb.Append(char.ToUpper(c));
                else if (filter.Contains(char.ToLower(c)))
                    sb.Append(char.ToLower(c));
            }
            str = sb.ToString();
        }
        
        Rect descRect = new(x, y - height * 0.5f, width * 0.25f, height * 0.5f);
        
        GUI.SetNextControlName("");
        
        GUI.Box(descRect, "Input a string");
        
        if (duration != -1)
        {
            float timeLeft = (startTime + duration) - Time.realtimeSinceStartup;
            Rect timerRect = rField;
            timerRect.y += timerRect.height + timerRect.height * 0.125f;
            timerRect.height *= 0.25f;
            timerRect.width *= timeLeft / duration;
            GUI.Box(timerRect, "");
        }
            
    }

    void Finish()
    {
        SceneSaverBL.currentInputter = null;
        taskCompletionSource.TrySetResult(str);
    }

    public static UniTask<string> GetStringAsync(int duration = 30, string startStr = "", string filter = "")
    {
        IMGUIInputField inputter = new(duration, startStr, filter);
        SceneSaverBL.currentInputter = inputter;
        return inputter.OnComplete;
    }
}
