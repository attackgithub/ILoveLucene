﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Caliburn.Micro;
using Core.Abstractions;
using Core.Extensions;
using ILoveLucene.Infrastructure;
using ILoveLucene.Views;
using Plugins.Shortcuts;
using ILog = Caliburn.Micro.ILog;

namespace ILoveLucene.ViewModels
{
    public class MainWindowViewModel : PropertyChangedBase
    {
        private readonly IAutoCompleteText _autoCompleteText;
        private readonly IGetActionsForItem _getActionsForItem;
        private readonly ILog _log;
        private CancellationTokenSource _cancelationTokenSource;

        public MainWindowViewModel(IAutoCompleteText autoCompleteText, IGetActionsForItem getActionsForItem, ILog log)
        {
            _autoCompleteText = autoCompleteText;
            _getActionsForItem = getActionsForItem;
            _log = log;
            _cancelationTokenSource = new CancellationTokenSource();
            _argumentCancelationTokenSource = new CancellationTokenSource();
            CommandOptions =
                new ListWithCurrentSelection<AutoCompletionResult.CommandResult>(
                    new AutoCompletionResult.CommandResult(new TextItem(string.Empty), null));
            ArgumentOptions = new ListWithCurrentSelection<string>();
            Result = CommandOptions.Current;
        }

        public void Execute(FrameworkElement source)
        {
            Task.Factory.StartNew(() =>
                                      {
                                          try
                                          {
                                              IItem result = null;
                                              if (ActionWithArguments != null)
                                              {
                                                  result = ActionWithArguments.ActOn(Result.Item, Arguments);
                                              }
                                              else
                                              {
                                                  result = SelectedAction.ActOn(Result.Item);
                                              }
                                              _autoCompleteText.LearnInputForCommandResult(Input, Result);
                                              _getActionsForItem.LearnActionForCommandResult(Input, SelectedAction, Result);

                                              result = result ?? NoReturnValue.Object;
                                              if(result != NoReturnValue.Object)
                                              {
                                                  Input = result.Text;
                                                  Description = result.Description;
                                                  Arguments = string.Empty;
                                                  Result = new AutoCompletionResult.CommandResult(result, null);
                                              }
                                              else
                                              {
                                                  Input = string.Empty;
                                                  Arguments = string.Empty;
                                                  // HACK
                                                  Caliburn.Micro.Execute.OnUIThread(() => ((MainWindowView)Window.GetWindow(source)).HideWindow());
                                              }
                                          }
                                          catch (Exception e)
                                          {
                                              Description = e.Message;
                                              _log.Error(e);
                                          }
                                      });
        }

        private ListWithCurrentSelection<AutoCompletionResult.CommandResult> _commandOptions;

        public ListWithCurrentSelection<AutoCompletionResult.CommandResult> CommandOptions
        {
            get { return _commandOptions; }
            set
            {
                _commandOptions = value;
                NotifyOfPropertyChange(() => CommandOptions);
            }
        }

        private ListWithCurrentSelection<string> _ArgumentOptions;

        public ListWithCurrentSelection<string> ArgumentOptions
        {
            get { return _ArgumentOptions; }
            set
            {
                _ArgumentOptions = value;
                NotifyOfPropertyChange(() => ArgumentOptions);
                NotifyOfPropertyChange(() => ArgumentOptionsVisibility);
            }
        }

        public Visibility ArgumentOptionsVisibility
        {
            get
            {
                return (Item is IActOnItemWithAutoCompletedArguments) && ArgumentOptions.Count > 0
                           ? Visibility.Visible
                           : Visibility.Hidden;
            }
        }

        public void ProcessShortcut(FrameworkElement source, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key == Key.Escape)
            {
                ((MainWindowView) Window.GetWindow(source)).Toggle();
                return;
            }

            if(eventArgs.Key == Key.Down || eventArgs.Key == Key.Up)
            {
                if (eventArgs.Key == Key.Down)
                    Result = CommandOptions.Next();
                else
                    Result = CommandOptions.Previous();

                Task.Factory.StartNew(() => SetActionsForResult(Result))
                    .GuardForException(SetError);
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.KeyboardDevice.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            int index;
            var str = new KeyConverter().ConvertToString(eventArgs.Key);
            if (int.TryParse(str, out index))
            {
                if (index == 0) index = 10;

                index -= 1;
                if (index < CommandOptions.Count)
                {
                    Result = CommandOptions.SetIndex(index);
                    Task.Factory.StartNew(() => SetActionsForResult(Result))
                        .GuardForException(SetError);
                    eventArgs.Handled = true;
                }
            }
        }

        private void SetActionsForResult(AutoCompletionResult.CommandResult result)
        {
            Actions = _getActionsForItem.ActionsForItem(result);
            SelectedAction = Actions.FirstOrDefault();
        }

        public void ProcessArgumentShortcut(FrameworkElement source, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key == Key.Escape)
            {
                ((MainWindowView) Window.GetWindow(source)).Toggle();
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Key.Down || eventArgs.Key == Key.Up)
            {
                if (eventArgs.Key == Key.Down)
                    Arguments = ArgumentOptions.Next();
                else
                    Arguments = ArgumentOptions.Previous();

                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.KeyboardDevice.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            var str = new KeyConverter().ConvertToString(eventArgs.Key);
            int index;
            if (int.TryParse(str, out index))
            {
                index -= 1;
                if (index < ArgumentOptions.Count)
                {
                    Arguments = ArgumentOptions.SetIndex(index);
                    eventArgs.Handled = true;
                }
            }
        }

        public void AutoComplete()
        {
            _cancelationTokenSource.Cancel();
            _cancelationTokenSource = new CancellationTokenSource();

            var token = _cancelationTokenSource.Token;
            Task.Factory.StartNew(() =>
                                      {
                                          var result = _autoCompleteText.Autocomplete(Input);

                                          token.ThrowIfCancellationRequested();

                                          _log.Info("Got autocompletion '{0}' for '{1}' with {2} alternatives",
                                                    result.AutoCompletedCommand, result.OriginalText,
                                                    result.OtherOptions.Count());

                                          if (result.HasAutoCompletion)
                                          {
                                              CommandOptions = new[] {result.AutoCompletedCommand}
                                                  .Concat(result.OtherOptions)
                                                  .ToListWithCurrentSelection();
                                          }
                                          else
                                          {
                                              CommandOptions =
                                                  new ListWithCurrentSelection<AutoCompletionResult.CommandResult>(
                                                      new AutoCompletionResult.CommandResult(new TextItem(Input),
                                                                                             null));
                                          }
                                          Result = CommandOptions.Current;
                                          ArgumentOptions = new ListWithCurrentSelection<string>();
                                          Arguments = string.Empty;

                                          SetActionsForResult(Result);
                                          AutoCompleteArgument();
                                      }, token)
                .GuardForException(SetError);
        }

        private void SetError(Exception e)
        {
            var aggregateException = e as AggregateException;
            if(aggregateException != null)
            {
                foreach (var exception in aggregateException.InnerExceptions)
                {
                    _log.Error(exception);
                }
            }
            Description = e.Message;
        }

        public void AutoCompleteArgument()
        {
            _argumentCancelationTokenSource.Cancel();
            _argumentCancelationTokenSource = new CancellationTokenSource();
            ArgumentOptions = new ListWithCurrentSelection<string>();

            var token = _argumentCancelationTokenSource.Token;
            var autoCompleteArgumentsCommand = SelectedAction as IActOnItemWithAutoCompletedArguments;
            if (autoCompleteArgumentsCommand == null)
                return;
            Task.Factory.StartNew(() =>
                                      {
                                          var result = autoCompleteArgumentsCommand.AutoCompleteArguments(Item, Arguments);

                                          token.ThrowIfCancellationRequested();
                                          _log.Info("Got autocompletion '{0}' for '{1}' with {2} alternatives",
                                                    result.AutoCompletedArgument, result.OriginalText,
                                                    result.OtherOptions.Count());
                                          if (result.HasAutoCompletion)
                                          {
                                              ArgumentOptions =
                                                  new[] {result.AutoCompletedArgument}
                                                      .Concat(result.OtherOptions)
                                                      .ToListWithCurrentSelection();
                                          }
                                          else
                                          {
                                              ArgumentOptions = new ListWithCurrentSelection<string>(Arguments);
                                          }

                                          Arguments = ArgumentOptions.Current;
                                      }, token)
                .GuardForException(e => Description = e.Message);
        }

        private string _description;

        public string Description
        {
            get { return _description; }
            set
            {
                _description = value;
                NotifyOfPropertyChange(() => Description);
            }
        }

        public IItem Item
        {
            get { return Result.Item; }
        }

        private AutoCompletionResult.CommandResult _result;

        public AutoCompletionResult.CommandResult Result
        {
            get { return _result; }
            set
            {
                _result = value;
                Description = Item.Description;

                NotifyOfPropertyChange(() => Result);
                NotifyOfPropertyChange(() => Item);
            }
        }

        private IEnumerable<IActOnItem> _actions;
        public IEnumerable<IActOnItem> Actions
        {
            get { return _actions; }
            set
            {
                _actions = value;
                NotifyOfPropertyChange(() => Actions);
            }
        }

        private IActOnItem _selectedAction;
        public IActOnItem SelectedAction
        {
            get { return _selectedAction; }
            set
            {
                _selectedAction = value;
                NotifyOfPropertyChange(() => SelectedAction);
                NotifyOfPropertyChange(() => ArgumentsVisible);
                NotifyOfPropertyChange(() => CanExecute);
            }
        }

        public IActOnItemWithArguments ActionWithArguments
        {
            get { return SelectedAction as IActOnItemWithArguments; }
        }

        private string _arguments;

        public string Arguments
        {
            get { return _arguments; }
            set
            {
                _arguments = value;
                NotifyOfPropertyChange(() => Arguments);
            }
        }

        public Visibility ArgumentsVisible
        {
            get { return (Result != null && ActionWithArguments != null)? Visibility.Visible : Visibility.Hidden; }
        }

        private string _input;
        private CancellationTokenSource _argumentCancelationTokenSource;

        public string Input
        {
            get { return _input; }
            set
            {
                _input = value;
                NotifyOfPropertyChange(() => Input);
                NotifyOfPropertyChange(() => CanExecute);
            }
        }

        public bool CanExecute
        {
            get { return !string.IsNullOrWhiteSpace(_input) && SelectedAction != null; }
        }
    }
}