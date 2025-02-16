﻿using System;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using HourBoostr.Settings.Objects;
using SteamKit2;
using SingleBoostr.Core.Misc;
using SingleBoostr.Core.Enums;

namespace HourBoostr.Settings.Ui
{
    public partial class AppHome : Form
    {
        /// <summary>
        /// Application settings
        /// </summary>
        private Config.Settings mSettings = new Config.Settings();

        /// <summary>
        /// The account we're currently modifying
        /// </summary>
        private SingleBoostr.Core.Objects.AccountSettings mActiveAccount;
        
        /// <summary>
        /// Constructor
        /// Sets placeholder text for text controls
        /// </summary>
        public AppHome()
        {
            InitializeComponent();

            /*Set placeholder text for text controls*/
            Utils.SendMessage(txtUsername.Handle, Messages.EM_SETCUEBANNER, 0, "Username");
            Utils.SendMessage(txtPassword.Handle, Messages.EM_SETCUEBANNER, 0, "Password");
            Utils.SendMessage(txtLoginKey.Handle, Messages.EM_SETCUEBANNER, 0, "Login Key");
            Utils.SendMessage(txtResponse.Handle, Messages.EM_SETCUEBANNER, 0, "Chat Response");
            Utils.SendMessage(txtGameItem.Handle, Messages.EM_SETCUEBANNER, 0, "Game ID");
        }

        /// <summary>
        /// Form load
        /// Finds HourBoostr.Settings.json and loads it
        /// </summary>
        private void mainForm_Load(object sender, EventArgs e)
        {
            /*If HourBoostr is running, give warning about saving settings*/
            var procs = Process.GetProcessesByName(Const.HourBoostr.NAME.ToLower());
            if (procs.Length > 0)
            {
                MessageBox.Show($"Settings will be overwritten when you close {Const.HourBoostr.NAME}.\n"
                    + $"I would not recommend making any changes here while {Const.HourBoostr.NAME} is running.\n"
                    + $"If you need to make any changes, make a copy of {Const.HourBoostr.SETTINGS_FILE}.", "Warning");
            }

            /*Find the HourBoostr.Settings.json file if it exists and load it up*/
            string file = Path.Combine(Application.StartupPath, Const.HourBoostr.SETTINGS_FILE);
            if (File.Exists(file))
            {
                try
                {
                    string fileContent = File.ReadAllText(file);

                    if (!string.IsNullOrWhiteSpace(fileContent))
                        mSettings = JsonConvert.DeserializeObject<Config.Settings>(fileContent);

                    cbCheckForUpdates.Checked = mSettings.CheckForUpdates;
                    cbHideToTray.Checked = mSettings.HideToTray;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not read the {Const.HourBoostr.SETTINGS_FILE}\n\n{ex.Message}", "Ruh roh!");
                }
            }

            /*Refresh the account list*/
            RefreshAccountList();
        }

        /// <summary>
        /// Adds all users to listbox
        /// </summary>
        private void RefreshAccountList()
        {
            /*Clear previous entries*/
            accountListBox.Items.Clear();

            /*Delete duplicates from accounts*/
            mSettings.Accounts = mSettings.Accounts.GroupBy(o => o.Details.Username.ToLower())
                .Select(y => y.First()).ToList();

            /*Go through all accounts and add them to the listbox
            Usernames that are empty gets an <empty> display name*/
            foreach (var user in mSettings.Accounts)
            {
                string username = user.Details.Username;
                if (string.IsNullOrWhiteSpace(username))
                    username = "<empty>";

                accountListBox.Items.Add(username);
            }

            /*Create an account if there's none in the settings*/
            if (accountListBox.Items.Count == 0)
            {
                mSettings.Accounts.Add(new SingleBoostr.Core.Objects.AccountSettings());
                RefreshAccountList();
            }

            SelectFirstAccount();
        }

        /// <summary>
        /// If an active account is set, save the changes made to it from the controls
        /// </summary>
        private void SaveCurrentAccount()
        {
            if (mActiveAccount == null)
                return;

            foreach (var user in mSettings.Accounts)
            {
                /*We'll match by username because only one account with a particular username is allowed at once*/
                /*If user attempts to add another account with a username that already exists we'll ask to merge them*/
                if (user.Details.Username == mActiveAccount.Details.Username)
                {
                    user.Details = new SingleBoostr.Core.Objects.AccountDetails()
                    {
                        Username = txtUsername.Text,
                        Password = txtPassword.Text,
                        LoginKey = txtLoginKey.Text
                    };

                    user.Details.Encrypt();

                    user.ShowOnlineStatus = cbOnlineStatus.Checked;
                    user.JoinSteamGroup = cbJoinGroup.Checked;
                    user.ConnectToSteamCommunity = cbCommunity.Checked;
                    user.RestartGamesEveryThreeHours = cbRestartGames.Checked;
                    user.IgnoreAccount = cbIgnoreAccount.Checked;
                    user.ChatResponse = txtResponse.Text;
                    user.Games = gameList.Items.Cast<int>().ToList();

                    switch (OSTypeComboBox.SelectedIndex)
                    {
                        case 0: user.OSType = EOSType.Windows10; break;
                        case 1: user.OSType = EOSType.Linux5x; break;
                        case 2: user.OSType = EOSType.MacOS109; break;
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Gets the user in list depending on accountListBox index
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void accountListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            /*Selected item can be null if the text is changed by txtUsername_TextChanged*/
            if (accountListBox.SelectedItem == null)
                return;
            
            /*Show the settings panel since we have an account to edit now*/
            if (!panelSettings.Visible)
                panelSettings.Visible = true;

            /*Save the current account before switching*/
            SaveCurrentAccount();

            /*Set the active account depending on index selected in the listbox*/
            mActiveAccount = mSettings.Accounts[accountListBox.SelectedIndex];

            /*Assign text settings*/
            txtUsername.Text = mActiveAccount.Details.Username;
            txtPassword.Text = mActiveAccount.Details.Password;
            txtLoginKey.Text = mActiveAccount.Details.LoginKey;
            txtResponse.Text = mActiveAccount.ChatResponse;

            /*Clear previous entries in game listbox and add new ones*/
            gameList.Items.Clear();
            mActiveAccount.Games.ForEach(o => gameList.Items.Add(o));

            /*Assign bool settings*/
            cbOnlineStatus.Checked = mActiveAccount.ShowOnlineStatus;
            cbJoinGroup.Checked = mActiveAccount.JoinSteamGroup;
            cbCommunity.Checked = mActiveAccount.ConnectToSteamCommunity;
            cbRestartGames.Checked = mActiveAccount.RestartGamesEveryThreeHours;
            cbIgnoreAccount.Checked = mActiveAccount.IgnoreAccount;

            switch (mActiveAccount.OSType)
            {
                case EOSType.Windows10: OSTypeComboBox.SelectedIndex = 0; break;
                case EOSType.Linux5x: OSTypeComboBox.SelectedIndex = 1; break;
                case EOSType.MacOS109: OSTypeComboBox.SelectedIndex = 2; break;
            }
        }

        /// <summary>
        /// Save settings when form is closed
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">FormClosingEventArgs</param>
        private void mainForm_FormClosing(object sender, FormClosingEventArgs e) => SaveAccountJson();
        
        /// <summary>
        /// Saves all accounts and exits the application
        /// </summary>
        private void SaveAccountJson()
        {
            SaveCurrentAccount();
            mSettings.Accounts.RemoveAll(o => string.IsNullOrWhiteSpace(o.Details.Username));

            mSettings.CheckForUpdates = cbCheckForUpdates.Checked;
            mSettings.HideToTray = cbHideToTray.Checked;

            try
            {
                string json = JsonConvert.SerializeObject(mSettings, Formatting.Indented);

                if (!string.IsNullOrWhiteSpace(json))
                    File.WriteAllText(Path.Combine(Application.StartupPath, "HourBoostr.Settings.json"), json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not update the HourBoostr.Settings.json file.\n\n{ex.Message}", "Uh oh...");
            }
        }

        /// <summary>
        /// Selects the last account in account list box
        /// </summary>
        private void SelectLastAccount() => accountListBox.SelectedIndex = accountListBox.Items.Count - 1;

        /// <summary>
        /// Selected the first account in account list box
        /// </summary>
        private void SelectFirstAccount()
        {
            if (accountListBox.Items.Count <= 0) return;
            accountListBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Creates a new account in the list
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblNewAccount_Click(object sender, EventArgs e)
        {
            /*We'll focus the listbox here to make sure that txtUsername Leave event fires*/
            accountListBox.Focus();

            mSettings.Accounts.Add(new SingleBoostr.Core.Objects.AccountSettings());
            RefreshAccountList();
            SelectLastAccount();
        }

        /// <summary>
        /// Updates the listbox account name when user leaves username control
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void txtUsername_Leave(object sender, EventArgs e)
        {
            /*Set the listbox name to empty if no username is set*/
            string username = txtUsername.Text;
            if (string.IsNullOrWhiteSpace(username))
                username = "<empty>";

            /*If it's the same username as we started with we don't want
            to make the following comparisons, so we'll end here*/
            if (((string)accountListBox.SelectedItem).ToLower() == username.ToLower())
                return;

            /*Check for duplicate entries*/
            if (mSettings.Accounts.Any(o => o.Details.Username.ToLower() == username.ToLower()))
            {
                MessageBox.Show("This account already exists in your account list.", "Hey, listen.");
                txtUsername.Text = string.Empty;
                txtUsername.Focus();
                return;
            }

            /*Update the listbox display value*/
            accountListBox.Items[accountListBox.SelectedIndex] = username;
        }

        /// <summary>
        /// If user types anything we'll auto-enable community and show online
        /// Since they are needed to respond
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void txtResponse_TextChanged(object sender, EventArgs e)
        {
            if (txtResponse.Text.Length > 0)
            {
                cbOnlineStatus.Checked = true;
                cbCommunity.Checked = true;
            }
        }

        /// <summary>
        /// Refreshes the gameListBox and fills it with items from mActiveAccount
        /// </summary>
        private void RefreshGameList()
        {
            gameList.Items.Clear();
            mActiveAccount.Games = mActiveAccount.Games.Distinct().ToList();
            mActiveAccount.Games.ForEach(o => gameList.Items.Add(o));
        }

        /// <summary>
        /// Allows for the contextMenu to open on right click
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">MouseEventArgs</param>
        private void accountListBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                accountListBox.SelectedIndex = accountListBox.IndexFromPoint(e.Location);
                if (accountListBox.SelectedIndex != -1)
                    accountListBoxMenu.Show(this, accountListBox.PointToClient(Cursor.Position));
            }
        }

        /// <summary>
        /// Cancels the opening of the menu if no listbox item is selected
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">CancelEventArgs</param>
        private void accountListBoxMenu_Opening(object sender, CancelEventArgs e)
        {
            if (accountListBox.SelectedItem != null) return;
            e.Cancel = true;
        }

        /// <summary>
        /// accountListBoxMenu remove button event
        /// Deletes selected account
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void removeAccountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult diagResult = DialogResult.Yes;

            /*Only show the dialog window if a username is set, else it's an empty account
            and I mean come on, who the fuck cares about that dude. Yeah, lol. Fuck them.*/
            if (!string.IsNullOrWhiteSpace(mActiveAccount.Details.Username))
            {
                diagResult = MessageBox.Show($"Do you want to delete user '{mActiveAccount.Details.Username}'?",
                    "Delete User", MessageBoxButtons.YesNo);
            }

            if (diagResult == DialogResult.No) return;

            /*Remove all accounts that matches that username*/
            mSettings.Accounts.RemoveAll(o => o.Details.Username == mActiveAccount.Details.Username);
            RefreshAccountList();
        }

        /// <summary>
        /// Adds a game id to the game list
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">KeyEventArgs</param>
        private void txtGameItem_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (int.TryParse(txtGameItem.Text, out int gameId))
                {
                    if (!mActiveAccount.Games.Contains(gameId))
                    {
                        mActiveAccount.Games.Add(gameId);
                        RefreshGameList();
                    }
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
                txtGameItem.Text = string.Empty;
            }
        }

        /// <summary>
        /// Allows for the contextMenu to open on right click
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">MouseEventArgs</param>
        private void gameList_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                gameList.SelectedIndex = gameList.IndexFromPoint(e.Location);
                if (gameList.SelectedIndex != -1)
                    gameListMenu.Show(this, gameList.PointToClient(Cursor.Position));
            }
        }

        /// <summary>
        /// Cancels the opening of the menu if no listbox item is selected
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">CancelEventArgs</param>
        private void gameListMenu_Opening(object sender, CancelEventArgs e)
        {
            if (gameList.SelectedItem != null) return;
            e.Cancel = true;
        }

        /// <summary>
        /// Removes an gameid from gameList box
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedItem = gameList.SelectedItem;

            mActiveAccount.Games.RemoveAll(o => o == (int)selectedItem);
            RefreshGameList();
        }

        private void SetPlayingToolStripMenuItem_Click(object sender, EventArgs e) => SetActiveGame();

        private void SetActiveGame()
        {
            if (mSettings.Accounts.Any())
            {
                gameList.SetSelected(gameList.SelectedIndex, true);
                mActiveAccount.ActiveGame = (uint)(int)gameList.SelectedItem;
                MessageBox.Show($"AppID: ({gameList.SelectedItem}) Will be set as currently playing", mActiveAccount.Details.Username);
                SaveAccountJson();
            }
            else
            {
                MessageBox.Show("No account loaded", "Error");
            }
        }

        private void txtStatus_TextChanged(object sender, EventArgs e)
        { 
            if (mSettings.Accounts.Any())
            { 
                mActiveAccount.ActiveGame = uint.MaxValue;
                mActiveAccount.CustomStatus = txtStatus.Text;  
            }
        }

        /// <summary>
        /// Prevents any typing in loginkey textbox
        /// Not using ReadOnly because I don't like how it greys it out
        /// Also PlaceHolder text won't work if readonly
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">KeyPressEventArgs</param>
        private void txtLoginKey_KeyPress(object sender, KeyPressEventArgs e) => e.Handled = true;
        
        /// <summary>
        /// lblRemoveLoginKey set textcolor to blue to show that it's highlighted
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblRemoveLoginKey_MouseEnter(object sender, EventArgs e) => lblRemoveLoginKey.ForeColor = SystemColors.Highlight;
        
        /// <summary>
        /// lblRemoveLoginKey set textcolor to blue to show that it's not highlighted
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblRemoveLoginKey_MouseLeave(object sender, EventArgs e)=> lblRemoveLoginKey.ForeColor = Color.Gray;
        
        /// <summary>
        /// Remove the loginkey text
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblRemoveLoginKey_Click(object sender, EventArgs e) => txtLoginKey.Text = string.Empty;
        
        /// <summary>
        /// lblFindGames set textcolor to blue to show that it's highlighted
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblFindGames_MouseEnter(object sender, EventArgs e) => lblFindGames.ForeColor = SystemColors.Highlight;
        
        /// <summary>
        /// lblFindGames set textcolor to blue to show that it's not highlighted
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblFindGames_MouseLeave(object sender, EventArgs e) => lblFindGames.ForeColor = Color.Gray;

        /// <summary>
        /// Opens game finder form
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblFindGames_Click(object sender, EventArgs e)
        {
            var gameWindow = new AppGames();
            gameWindow.ShowDialog();

            var games = gameWindow.mGamesSelected;
            if (games.Count > 0)
            {
                foreach (var game in games)
                    mActiveAccount.Games.Add(game.id);

                RefreshGameList();
            }

            gameWindow.Close();
        }

        /// <summary>
        /// Check if both Community and OnlineStatus are checked
        /// Then we can enable the Chat Response textbox
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void cbCommunity_CheckedChanged(object sender, EventArgs e) => txtResponse.Enabled = (cbOnlineStatus.Checked && cbCommunity.Checked);
        
        /// <summary>
        /// Check if both Community and OnlineStatus are checked
        /// Then we can enable the Chat Response textbox
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void cbOnlineStatus_CheckedChanged(object sender, EventArgs e) => txtResponse.Enabled = (cbOnlineStatus.Checked && cbCommunity.Checked);

        /// <summary>
        /// Starts HourBoostr if it exists in this folder
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblStartBooster_Click(object sender, EventArgs e)
        {
            if (mSettings.Accounts.Count == 0)
            {
                MessageBox.Show("No account loaded", "Error");
                return;
            }

            if (File.Exists(Path.Combine(Application.StartupPath, Const.HourBoostr.IDLER_EXE)))
            {
                var proc = new Process();
                proc.StartInfo.FileName = Const.HourBoostr.IDLER_EXE;
                proc.StartInfo.WindowStyle = ProcessWindowStyle.Normal;

                SaveAccountJson();
                if (proc.Start())
                {
                    Thread.Sleep(1000);
                    Environment.Exit(1);
                }
            }
            else
            {
                MessageBox.Show($"Can't find {Const.HourBoostr.IDLER_EXE}", "Woops..");
            }
        }

        /// <summary>
        /// lblStartBooster set textcolor to blue to show that it's highlighted
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblStartBooster_MouseEnter(object sender, EventArgs e) => lblStartBooster.ForeColor = SystemColors.Highlight;

        /// <summary>
        /// lblStartBooster set textcolor to blue to show that it's not highlighted
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void lblStartBooster_MouseLeave(object sender, EventArgs e) => lblStartBooster.ForeColor = Color.Gray;
    }
}
