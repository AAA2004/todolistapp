using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections;
using System.Collections.Generic;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using System.Threading.Tasks;

[RequireComponent(typeof(UIDocument))]
public class ReminderUIManager : MonoBehaviour
{
    private VisualElement loginPage, weekPage, dayPage, detailsPage, progressPage;
    private VisualElement notificationPopup;
    private Label notificationText, percentageLabel, fractionLabel, loginErrorLabel;
    private TextField usernameInput, passwordInput, taskNameInput, taskDescriptionInput;
    private DropdownField priorityDropdown, timeDropdown;
    private ScrollView taskListScroll;
    
    // State Tracking
    private string currentUsername = "";
    private string currentDaySelected = "";
    private string currentTaskName = "";
    private readonly string[] shortDays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private Queue<string> popupQueue = new Queue<string>();
    private bool isPopupShowing = false;

    // --- FIREBASE VARIABLES ---
    private DatabaseReference dbReference;
    private bool isFirebaseReady = false;

    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        loginPage = root.Q<VisualElement>("LoginPage");
        weekPage = root.Q<VisualElement>("WeekPage");
        dayPage = root.Q<VisualElement>("DayPage");
        detailsPage = root.Q<VisualElement>("DetailsPage");
        progressPage = root.Q<VisualElement>("ProgressPage");
        notificationPopup = root.Q<VisualElement>("NotificationPopup");
        notificationText = root.Q<Label>("NotificationText");
        usernameInput = root.Q<TextField>("UsernameInput");
        passwordInput = root.Q<TextField>("PasswordInput");
        loginErrorLabel = root.Q<Label>("LoginErrorLabel");
        taskNameInput = root.Q<TextField>("TaskNameInput");
        taskDescriptionInput = root.Q<TextField>("TaskDescriptionInput");
        priorityDropdown = root.Q<DropdownField>("PriorityDropdown");
        timeDropdown = root.Q<DropdownField>("TimeDropdown");
        taskListScroll = root.Q<ScrollView>("TaskListScroll");
        percentageLabel = root.Q<Label>("PercentageLabel");
        fractionLabel = root.Q<Label>("FractionLabel");

        // UI Wiring
        root.Q<Button>("LoginButton").clicked += TryLogin;
        root.Q<Button>("SignupButton").clicked += CreateAccount;
        root.Q<Button>("LogoutButton").clicked += Logout;

        string[] fullDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        foreach (string day in fullDays)
        {
            string dayName = day; 
            root.Q<Button>($"Btn{dayName}").clicked += () => SelectDay(dayName);
        }

        root.Q<Button>("DayBackButton").clicked += () => SwitchToPage(weekPage);
        root.Q<Button>("AddNewTaskBtn").clicked += () => SelectTask("New Task");
        root.Q<Button>("DetailsBackButton").clicked += () => SwitchToPage(dayPage);
        root.Q<Button>("SaveTaskButton").clicked += SaveTaskData;
        root.Q<Button>("ViewProgressBtn").clicked += ShowProgressPage;
        root.Q<Button>("ProgressBackButton").clicked += () => SwitchToPage(weekPage);

        // INITIALIZE FIREBASE
        InitializeFirebase();

        SwitchToPage(loginPage);
        InvokeRepeating(nameof(CheckForReminders), 1f, 60f);
    }

    // --- FIREBASE CORE METHODS ---
    private void InitializeFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
            if (task.Result == DependencyStatus.Available)
            {
                FirebaseApp.DefaultInstance.Options.DatabaseUrl = new Uri("https://to-do-list-5acf0-default-rtdb.europe-west1.firebasedatabase.app/");
                dbReference = FirebaseDatabase.DefaultInstance.RootReference;
                isFirebaseReady = true;
                Debug.Log("Firebase successfully initialized and ready!");
            }
            else
            {
                Debug.LogError($"Could not resolve all Firebase dependencies: {task.Result}");
            }
        });
    }

    private void SaveDataToCloud(string key, string value)
    {
        // 1. Backup to Local Storage instantly
        PlayerPrefs.SetString(key, value);
        PlayerPrefs.Save();

        // 2. Push to Main Cloud Storage
        if (isFirebaseReady && !string.IsNullOrEmpty(currentUsername))
        {
            dbReference.Child("Users").Child(currentUsername).Child(key).SetValueAsync(value);
        }
    }

    private void DeleteDataFromCloud(string key)
    {
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();

        if (isFirebaseReady && !string.IsNullOrEmpty(currentUsername))
        {
            dbReference.Child("Users").Child(currentUsername).Child(key).RemoveValueAsync();
        }
    }

    private void SyncCloudToLocal()
    {
        if (!isFirebaseReady) return;

        // Fetch all data for the logged-in user and overwrite the local backup
        dbReference.Child("Users").Child(currentUsername).GetValueAsync().ContinueWithOnMainThread(task => {
            if (task.IsCompleted && task.Result.Exists)
            {
                DataSnapshot snapshot = task.Result;
                foreach (DataSnapshot child in snapshot.Children)
                {
                    PlayerPrefs.SetString(child.Key, child.Value.ToString());
                }
                PlayerPrefs.Save();
                Debug.Log("Successfully synced from Firebase Cloud to Local Backup.");
            }
        });
    }

    private void SwitchToPage(VisualElement targetPage)
    {
        loginPage.style.display = DisplayStyle.None;
        weekPage.style.display = DisplayStyle.None;
        dayPage.style.display = DisplayStyle.None;
        detailsPage.style.display = DisplayStyle.None;
        progressPage.style.display = DisplayStyle.None;

        targetPage.style.display = DisplayStyle.Flex;
        loginErrorLabel.style.display = DisplayStyle.None; 
    }

    // --- ACCOUNT LOGIC ---
    private void CreateAccount()
    {
        if (string.IsNullOrEmpty(usernameInput.value) || string.IsNullOrEmpty(passwordInput.value))
        {
            loginErrorLabel.text = "Please enter username and password.";
            loginErrorLabel.style.display = DisplayStyle.Flex;
            return;
        }

        currentUsername = usernameInput.value;
        SaveDataToCloud("Account_Password", passwordInput.value);
        
        SyncCloudToLocal(); // Pull any existing data if they re-registered an old name
        SwitchToPage(weekPage);
    }

    private void TryLogin()
    {
        string localPassword = PlayerPrefs.GetString("Account_Password", "");
        
        // If local authentication fails, we should ideally check Firebase, 
        // but for this prototype, we rely on the local cache sync.
        if (localPassword != "" && localPassword == passwordInput.value)
        {
            currentUsername = usernameInput.value;
            SyncCloudToLocal(); // Download latest tasks from another device
            SwitchToPage(weekPage);
        }
        else
        {
            // Simple fallback to attempt Firebase check directly if local is missing
            if (isFirebaseReady)
            {
                dbReference.Child("Users").Child(usernameInput.value).Child("Account_Password").GetValueAsync().ContinueWithOnMainThread(task => {
                    if (task.IsCompleted && task.Result.Exists && task.Result.Value.ToString() == passwordInput.value)
                    {
                        currentUsername = usernameInput.value;
                        SyncCloudToLocal();
                        SwitchToPage(weekPage);
                    }
                    else
                    {
                        loginErrorLabel.text = "Invalid Username or Password.";
                        loginErrorLabel.style.display = DisplayStyle.Flex;
                    }
                });
            }
            else
            {
                loginErrorLabel.text = "Invalid Username or Password (Offline).";
                loginErrorLabel.style.display = DisplayStyle.Flex;
            }
        }
    }

    private void Logout()
    {
        currentUsername = "";
        usernameInput.value = "";
        passwordInput.value = "";
        SwitchToPage(loginPage);
    }

    // --- DAY & TASK LOGIC ---
    private void SelectDay(string dayName)
    {
        currentDaySelected = dayName;
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        root.Q<Label>("DayTitleLabel").text = dayName;
        
        LoadTasksIntoUI(dayName);
        SwitchToPage(dayPage);
    }

    private void LoadTasksIntoUI(string dayName)
    {
        taskListScroll.Clear(); 
        string shortDay = dayName.Substring(0, 3);
        string savedTasksList = PlayerPrefs.GetString(currentUsername + "_Tasks_" + shortDay, "");

        if (string.IsNullOrEmpty(savedTasksList)) return;

        string[] tasks = savedTasksList.Split('|');
        ColorUtility.TryParseHtmlString("#4CAF50", out Color greenColor);
        ColorUtility.TryParseHtmlString("#E53935", out Color redColor);

        foreach (string task in tasks)
        {
            if (string.IsNullOrEmpty(task)) continue;

            string baseKey = currentUsername + "_" + shortDay + "_" + task;
            string state = PlayerPrefs.GetString(baseKey + "_State", "Pending");
            string time = PlayerPrefs.GetString(baseKey + "_Time", "12:00");

            if (state == "Pending" && IsTimePassed(shortDay, time))
            {
                state = "Missed";
                SaveDataToCloud(baseKey + "_State", state);
            }

            if (state == "Completed") continue;

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 30;
            row.style.height = 120;

            Button taskBtn = new Button();
            taskBtn.text = $"{task} ({time}) - {state}"; 
            taskBtn.style.flexGrow = 1;
            taskBtn.style.backgroundColor = state == "Missed" ? new StyleColor(redColor) : new StyleColor(greenColor);
            taskBtn.style.borderTopLeftRadius = 40;
            taskBtn.style.borderBottomLeftRadius = 40;
            taskBtn.style.color = new StyleColor(Color.white);
            taskBtn.style.borderTopWidth = 0; taskBtn.style.borderBottomWidth = 0; taskBtn.style.borderLeftWidth = 0; taskBtn.style.borderRightWidth = 0;

            string capturedTaskName = task;
            taskBtn.clicked += () => SelectTask(capturedTaskName);

            Button doneBtn = new Button();
            doneBtn.text = "✓";
            doneBtn.style.width = 150;
            doneBtn.style.backgroundColor = new StyleColor(Color.white);
            doneBtn.style.color = new StyleColor(greenColor);
            doneBtn.style.fontSize = 60;
            doneBtn.style.borderTopRightRadius = 40;
            doneBtn.style.borderBottomRightRadius = 40;

            doneBtn.clicked += () => MarkTaskAsCompleted(shortDay, capturedTaskName);

            row.Add(taskBtn);
            row.Add(doneBtn); 
            taskListScroll.Add(row);
        }
    }

    private void MarkTaskAsCompleted(string shortDay, string taskName)
    {
        SaveDataToCloud(currentUsername + "_" + shortDay + "_" + taskName + "_State", "Completed");
        LoadTasksIntoUI(currentDaySelected);
    }

    private void SelectTask(string taskName)
    {
        currentTaskName = taskName;
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        root.Q<Label>("TaskDetailTitle").text = taskName == "New Task" ? "Create Task" : "Edit Task";
        taskNameInput.value = taskName == "New Task" ? "" : taskName;

        string currentShortDay = currentDaySelected.Substring(0, 3);

        if (taskName != "New Task")
        {
            string baseKey = currentUsername + "_" + currentShortDay + "_" + taskName;
            taskDescriptionInput.value = PlayerPrefs.GetString(baseKey + "_Desc", "");
            priorityDropdown.value = PlayerPrefs.GetString(baseKey + "_Priority", "Important");
            timeDropdown.value = PlayerPrefs.GetString(baseKey + "_Time", "12:00");
        }
        else
        {
            taskDescriptionInput.value = "";
            priorityDropdown.value = "Important";
            timeDropdown.value = "12:00";
        }

        foreach (string day in shortDays)
        {
            Toggle toggle = root.Q<Toggle>($"Toggle{day}");
            if (toggle != null)
            {
                if (taskName == "New Task") toggle.value = (day == currentShortDay);
                else toggle.value = PlayerPrefs.GetString(currentUsername + "_Tasks_" + day, "").Contains(taskName);
            }
        }
        SwitchToPage(detailsPage);
    }

    private void SaveTaskData()
    {
        string newTaskName = taskNameInput.value.Trim();
        if (string.IsNullOrEmpty(newTaskName)) return;

        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        if (currentTaskName != "New Task" && currentTaskName != newTaskName)
        {
            string listKey = currentUsername + "_Tasks_" + currentDaySelected.Substring(0, 3);
            string oldList = PlayerPrefs.GetString(listKey, "");
            List<string> oldTasks = new List<string>(oldList.Split('|'));
            oldTasks.Remove(currentTaskName);
            SaveDataToCloud(listKey, string.Join("|", oldTasks));
        }

        foreach (string day in shortDays)
        {
            Toggle dayToggle = root.Q<Toggle>($"Toggle{day}");
            if (dayToggle != null && dayToggle.value)
            {
                string listKey = currentUsername + "_Tasks_" + day;
                string tasksList = PlayerPrefs.GetString(listKey, "");
                
                if (!tasksList.Contains(newTaskName))
                {
                    if (tasksList.Length > 0) tasksList += "|";
                    tasksList += newTaskName;
                    SaveDataToCloud(listKey, tasksList);
                }

                string baseKey = currentUsername + "_" + day + "_" + newTaskName;
                SaveDataToCloud(baseKey + "_Desc", taskDescriptionInput.value);
                SaveDataToCloud(baseKey + "_Priority", priorityDropdown.value);
                SaveDataToCloud(baseKey + "_Time", timeDropdown.value);
                
                if (string.IsNullOrEmpty(PlayerPrefs.GetString(baseKey + "_State", "")))
                    SaveDataToCloud(baseKey + "_State", "Pending");
            }
        }

        LoadTasksIntoUI(currentDaySelected);
        SwitchToPage(dayPage);
        CheckForReminders();
    }

    // --- TIME & STATISTICS LOGIC ---
    private int GetDayIndex(string shortDay) { return Array.IndexOf(shortDays, shortDay); }

    private int GetTodayIndex()
    {
        int day = (int)DateTime.Now.DayOfWeek - 1; 
        return day < 0 ? 6 : day; 
    }

    private bool IsTimePassed(string taskShortDay, string taskTime)
    {
        int taskDayIndex = GetDayIndex(taskShortDay);
        int todayIndex = GetTodayIndex();

        if (taskDayIndex < todayIndex) return true; 
        if (taskDayIndex == todayIndex)
        {
            TimeSpan current = DateTime.Now.TimeOfDay;
            TimeSpan target = TimeSpan.Parse(taskTime + ":00");
            if (current > target) return true; 
        }
        return false;
    }

    private void ShowProgressPage()
    {
        int totalTasks = 0;
        int completedTasks = 0;

        foreach (string day in shortDays)
        {
            string savedTasksList = PlayerPrefs.GetString(currentUsername + "_Tasks_" + day, "");
            if (string.IsNullOrEmpty(savedTasksList)) continue;

            string[] tasks = savedTasksList.Split('|');
            foreach (string task in tasks)
            {
                if (string.IsNullOrEmpty(task)) continue;
                totalTasks++;
                
                if (PlayerPrefs.GetString(currentUsername + "_" + day + "_" + task + "_State", "") == "Completed")
                {
                    completedTasks++;
                }
            }
        }

        fractionLabel.text = $"{completedTasks} / {totalTasks} Tasks";
        int percentage = totalTasks > 0 ? Mathf.RoundToInt(((float)completedTasks / totalTasks) * 100f) : 0;
        percentageLabel.text = $"{percentage}%";

        SwitchToPage(progressPage);
    }

    // --- NOTIFICATION LOGIC ---
    private void CheckForReminders()
    {
        if (string.IsNullOrEmpty(currentUsername)) return;

        string todayShort = shortDays[GetTodayIndex()];
        string savedTasksList = PlayerPrefs.GetString(currentUsername + "_Tasks_" + todayShort, "");
        if (string.IsNullOrEmpty(savedTasksList)) return;

        string[] tasks = savedTasksList.Split('|');
        TimeSpan current = DateTime.Now.TimeOfDay;

        foreach (string task in tasks)
        {
            if (string.IsNullOrEmpty(task)) continue;
            string baseKey = currentUsername + "_" + todayShort + "_" + task;
            
            if (PlayerPrefs.GetString(baseKey + "_State", "") == "Completed") continue;
            
            TimeSpan target = TimeSpan.Parse(PlayerPrefs.GetString(baseKey + "_Time", "12:00") + ":00");
            double minutesUntil = (target - current).TotalMinutes;

            if (minutesUntil > 0 && minutesUntil <= 60 && PlayerPrefs.GetInt(baseKey + "_Notified", 0) == 0)
            {
                popupQueue.Enqueue($"Upcoming: {task} at {PlayerPrefs.GetString(baseKey + "_Time", "")}");
                SaveDataToCloud(baseKey + "_Notified", "1"); 
            }
        }

        if (popupQueue.Count > 0 && !isPopupShowing)
        {
            StartCoroutine(ShowNextPopup());
        }
    }

    private IEnumerator ShowNextPopup()
    {
        isPopupShowing = true;
        while (popupQueue.Count > 0)
        {
            notificationText.text = popupQueue.Dequeue();
            notificationPopup.style.display = DisplayStyle.Flex;
            
            yield return new WaitForSeconds(10f); 
            
            notificationPopup.style.display = DisplayStyle.None;
            yield return new WaitForSeconds(1f); 
        }
        isPopupShowing = false;
    }
}