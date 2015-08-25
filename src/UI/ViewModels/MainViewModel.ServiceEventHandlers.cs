﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using JuliusSweetland.OptiKey.Enums;
using JuliusSweetland.OptiKey.Extensions;
using JuliusSweetland.OptiKey.Models;
using JuliusSweetland.OptiKey.Properties;
using JuliusSweetland.OptiKey.UI.ViewModels.Keyboards;
using Size = JuliusSweetland.OptiKey.UI.ViewModels.Keyboards.SizeAndPosition;

namespace JuliusSweetland.OptiKey.UI.ViewModels
{
    public partial class MainViewModel
    {
        public void AttachServiceEventHandlers()
        {
            Log.Debug("AttachServiceEventHandlers called.");

            if (errorNotifyingServices != null)
            {
                errorNotifyingServices.ForEach(s => s.Error += HandleServiceError);
            }

            inputService.PointsPerSecond += (o, value) => { PointsPerSecond = value; };

            inputService.CurrentPosition += (o, tuple) =>
            {
                CurrentPositionPoint = tuple.Item1;
                CurrentPositionKey = tuple.Item2;

                if (keyboardService.KeyDownStates[KeyValues.MouseMagneticCursorKey].Value.IsDownOrLockedDown())
                {
                    mouseService.MoveTo(CurrentPositionPoint);
                }
            };

            inputService.SelectionProgress += (o, progress) =>
            {
                if (progress.Item1 == null
                    && progress.Item2 == 0)
                {
                    ResetSelectionProgress(); //Reset all keys
                }
                else if (progress.Item1 != null)
                {
                    if (SelectionMode == SelectionModes.Key
                        && progress.Item1.Value.KeyValue != null)
                    {
                        keyboardService.KeySelectionProgress[progress.Item1.Value.KeyValue.Value] =
                            new NotifyingProxy<double>(progress.Item2);
                    }
                    else if (SelectionMode == SelectionModes.Point)
                    {
                        PointSelectionProgress = new Tuple<Point, double>(progress.Item1.Value.Point, progress.Item2);
                    }
                }
            };

            inputService.Selection += (o, value) =>
            {
                Log.Debug("Selection event received from InputService.");

                SelectionResultPoints = null; //Clear captured points from previous SelectionResult event

                if (SelectionMode == SelectionModes.Key
                    && value.KeyValue != null)
                {
                    if (!capturingStateManager.CapturingMultiKeySelection)
                    {
                        audioService.PlaySound(Settings.Default.KeySelectionSoundFile, Settings.Default.KeySelectionSoundVolume);
                    }

                    if (KeySelection != null)
                    {
                        Log.DebugFormat("Firing KeySelection event with KeyValue '{0}'", value.KeyValue.Value);
                        KeySelection(this, value.KeyValue.Value);
                    }
                }
                else if (SelectionMode == SelectionModes.Point)
                {
                    if (PointSelection != null)
                    {
                        PointSelection(this, value.Point);

                        if (nextPointSelectionAction != null)
                        {
                            Log.DebugFormat("Executing nextPointSelectionAction delegate with point '{0}'", value.Point);
                            nextPointSelectionAction(value.Point);
                        }
                    }
                }
            };

            inputService.SelectionResult += (o, tuple) =>
            {
                Log.Debug("SelectionResult event received from InputService.");

                var points = tuple.Item1;
                var singleKeyValue = tuple.Item2 != null || tuple.Item3 != null
                    ? new KeyValue { FunctionKey = tuple.Item2, String = tuple.Item3 }
                    : (KeyValue?)null;
                var multiKeySelection = tuple.Item4;

                SelectionResultPoints = points; //Store captured points from SelectionResult event (displayed for debugging)

                if (SelectionMode == SelectionModes.Key
                    && (singleKeyValue != null || (multiKeySelection != null && multiKeySelection.Any())))
                {
                    KeySelectionResult(singleKeyValue, multiKeySelection);
                }
                else if (SelectionMode == SelectionModes.Point)
                {
                    //SelectionResult event has no real meaning when dealing with point selection
                }
            };

            inputService.PointToKeyValueMap = pointToKeyValueMap;
            inputService.SelectionMode = SelectionMode;
        }
        
        private void KeySelectionResult(KeyValue? singleKeyValue, List<string> multiKeySelection)
        {
            //Single key string
            if (singleKeyValue != null
                && !string.IsNullOrEmpty(singleKeyValue.Value.String))
            {
                Log.DebugFormat("KeySelectionResult received with string value '{0}'", singleKeyValue.Value.String.ConvertEscapedCharsToLiterals());
                textOutputService.ProcessSingleKeyText(singleKeyValue.Value.String);
            }

            //Single key function key
            if (singleKeyValue != null
                && singleKeyValue.Value.FunctionKey != null)
            {
                Log.DebugFormat("KeySelectionResult received with function key value '{0}'", singleKeyValue.Value.FunctionKey);
                HandleFunctionKeySelectionResult(singleKeyValue.Value);
            }

            //Multi key selection
            if (multiKeySelection != null
                && multiKeySelection.Any())
            {
                Log.DebugFormat("KeySelectionResult received with '{0}' multiKeySelection results", multiKeySelection.Count);
                textOutputService.ProcessMultiKeyTextAndSuggestions(multiKeySelection);
            }
        }

        private void HandleFunctionKeySelectionResult(KeyValue singleKeyValue)
        {
            if (singleKeyValue.FunctionKey != null)
            {
                keyboardService.ProgressKeyDownState(singleKeyValue);

                var currentKeyboard = Keyboard;

                switch (singleKeyValue.FunctionKey.Value)
                {
                    case FunctionKeys.AddToDictionary:
                        AddTextToDictionary();
                        break;

                    case FunctionKeys.AlphaKeyboard:
                        Log.Debug("Changing keyboard to Alpha.");
                        Keyboard = new Alpha();
                        break;

                    case FunctionKeys.ConversationKeyboard:
                        Log.Debug("Changing keyboard to Conversation.");
                        Keyboard = new Conversation(() =>
                        {
                            Log.Debug("Restoring window.");
                            mainWindowManipulationService.Restore();
                            Keyboard = currentKeyboard;
                        });
                        Log.Debug("Maximising window.");
                        mainWindowManipulationService.Maximise();
                        break;

                    case FunctionKeys.Diacritic1Keyboard:
                        Log.Debug("Changing keyboard to Diacritic1.");
                        Keyboard = new Diacritic1();
                        break;

                    case FunctionKeys.Diacritic2Keyboard:
                        Log.Debug("Changing keyboard to Diacritic2.");
                        Keyboard = new Diacritic2();
                        break;

                    case FunctionKeys.Diacritic3Keyboard:
                        Log.Debug("Changing keyboard to Diacritic3.");
                        Keyboard = new Diacritic3();
                        break;

                    case FunctionKeys.BackFromKeyboard:
                        Log.Debug("Navigating back from keyboard.");
                        var navigableKeyboard = Keyboard as IBackAction;
                        if (navigableKeyboard != null && navigableKeyboard.BackAction != null)
                        {
                            navigableKeyboard.BackAction();
                        }
                        else
                        {
                            Keyboard = new Alpha();
                        }
                        break;

                    case FunctionKeys.Calibrate:
                        if (CalibrationService != null)
                        {
                            Log.Debug("Calibrate requested.");
                            
                            var question = CalibrationService.CanBeCompletedWithoutManualIntervention
                                ? "Are you sure you would like to re-calibrate?"
                                : "Calibration cannot be completed without manual intervention, e.g. having to use a mouse. You may be stuck in the calibration process if you cannot manually interact with your computer.\nAre you sure you would like to re-calibrate?";
                            
                            Keyboard = new YesNoQuestion(
                                question,
                                () =>
                                {
                                    inputService.RequestSuspend();
                                    Keyboard = currentKeyboard;
                                    CalibrateRequest.Raise(new NotificationWithCalibrationResult(), calibrationResult =>
                                    {
                                        if (calibrationResult.Success)
                                        {
                                            audioService.PlaySound(Settings.Default.InfoSoundFile, Settings.Default.InfoSoundVolume);
                                            RaiseToastNotification("Success", calibrationResult.Message, NotificationTypes.Normal, () => inputService.RequestResume());
                                        }
                                        else
                                        {
                                            audioService.PlaySound(Settings.Default.ErrorSoundFile, Settings.Default.ErrorSoundVolume);
                                            RaiseToastNotification("Uh-oh!", calibrationResult.Exception != null
                                                    ? calibrationResult.Exception.Message
                                                    : calibrationResult.Message ?? "Something went wrong, but I don't know what - please check the logs", 
                                                NotificationTypes.Error, 
                                                () => inputService.RequestResume());
                                        }
                                    });
                                },
                                () =>
                                {
                                    Keyboard = currentKeyboard;
                                });
                        }
                        break;

                    case FunctionKeys.CollapseDock:
                        Log.Debug("Collapsing dock.");
                        mainWindowManipulationService.ResizeDockToCollapsed();
                        if (Keyboard is ViewModels.Keyboards.Mouse)
                        {
                            Settings.Default.MouseKeyboardDockSize = DockSizes.Collapsed;
                        }
                        break;

                    case FunctionKeys.Currencies1Keyboard:
                        Log.Debug("Changing keyboard to Currencies1.");
                        Keyboard = new Currencies1();
                        break;

                    case FunctionKeys.Currencies2Keyboard:
                        Log.Debug("Changing keyboard to Currencies2.");
                        Keyboard = new Currencies2();
                        break;

                    case FunctionKeys.DecreaseOpacity:
                        Log.Debug("Decreasing opacity.");
                        mainWindowManipulationService.ChangeOpacity(false);
                        break;

                    case FunctionKeys.ExpandDock:
                        Log.Debug("Expanding dock.");
                        mainWindowManipulationService.ResizeDockToFull();
                        if (Keyboard is ViewModels.Keyboards.Mouse)
                        {
                            Settings.Default.MouseKeyboardDockSize = DockSizes.Full;
                        }
                        break;

                    case FunctionKeys.ExpandToBottom:
                        Log.DebugFormat("Expanding to bottom by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.Bottom, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ExpandToBottomAndLeft:
                        Log.DebugFormat("Expanding to bottom and left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.BottomLeft, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ExpandToBottomAndRight:
                        Log.DebugFormat("Expanding to bottom and right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.BottomRight, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ExpandToLeft:
                        Log.DebugFormat("Expanding to left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.Left, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ExpandToRight:
                        Log.DebugFormat("Expanding to right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.Right, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ExpandToTop:
                        Log.DebugFormat("Expanding to top by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.Top, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ExpandToTopAndLeft:
                        Log.DebugFormat("Expanding to top and left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.TopLeft, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ExpandToTopAndRight:
                        Log.DebugFormat("Expanding to top and right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Expand(ExpandToDirections.TopRight, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.IncreaseOpacity:
                        Log.Debug("Increasing opacity.");
                        mainWindowManipulationService.ChangeOpacity(true);
                        break;

                    case FunctionKeys.MenuKeyboard:
                        Log.Debug("Changing keyboard to Menu.");
                        Keyboard = new Menu(() => Keyboard = currentKeyboard);
                        break;

                    case FunctionKeys.Minimise:
                        Log.Debug("Minimising window.");
                        mainWindowManipulationService.Minimise();
                        Log.Debug("Changing keyboard to Minimised.");
                        Keyboard = new Minimised(() =>
                        {
                            Log.Debug("Restoring window.");
                            mainWindowManipulationService.Restore();
                            Keyboard = currentKeyboard;
                        });
                        break;

                    case FunctionKeys.MouseDrag:
                        Log.Debug("Mouse drag selected.");
                        SetupFinalClickAction(firstFinalPoint =>
                        {
                            if (firstFinalPoint != null)
                            {
                                audioService.PlaySound(Settings.Default.MouseDownSoundFile, Settings.Default.MouseDownSoundVolume);
                                
                                //This class reacts to the point selection event AFTER the MagnifyPopup reacts to it.
                                //This means that if the MagnifyPopup sets the nextPointSelectionAction from the
                                //MagnifiedPointSelectionAction then it will be called immediately i.e. for the same point.
                                //The workaround is to set the nextPointSelectionAction to a lambda which sets the NEXT
                                //nextPointSelectionAction. This means the immediate call to the lambda just sets up the
                                //delegate for the subsequent call.
                                nextPointSelectionAction = repeatFirstClickOrSecondClickAction =>
                                {
                                    Action<Point> deferIfMagnifyingElseDoNow = repeatFirstClickOrSecondClickPoint =>
                                    {
                                        Action<Point?> secondFinalClickAction = secondFinalPoint =>
                                        {
                                            if (secondFinalPoint != null)
                                            {
                                                Action<Point, Point> simulateDrag = (fp1, fp2) =>
                                                {
                                                    Log.DebugFormat("Performing mouse drag between points ({0},{1}) and {2},{3}).", fp1.X, fp1.Y, fp2.X, fp2.Y);
                                                    mouseService.MoveTo(fp1);
                                                    mouseService.LeftButtonDown();
                                                    audioService.PlaySound(Settings.Default.MouseUpSoundFile, Settings.Default.MouseUpSoundVolume);
                                                    mouseService.MoveTo(fp2);
                                                    mouseService.LeftButtonUp();
                                                };

                                                lastMouseActionStateManager.LastMouseAction =
                                                    () => simulateDrag(firstFinalPoint.Value, secondFinalPoint.Value);
                                                simulateDrag(firstFinalPoint.Value, secondFinalPoint.Value);
                                            }

                                            ResetAndCleanupAfterMouseAction();
                                        };

                                        if (keyboardService.KeyDownStates[KeyValues.MouseMagnifierKey].Value.IsDownOrLockedDown())
                                        {
                                            ShowCursor = false; //See MouseMoveAndLeftClick case for explanation of this
                                            MagnifiedPointSelectionAction = secondFinalClickAction;
                                            MagnifyAtPoint = repeatFirstClickOrSecondClickPoint;
                                            ShowCursor = true;
                                        }
                                        else
                                        {
                                            secondFinalClickAction(repeatFirstClickOrSecondClickPoint);
                                        }

                                        nextPointSelectionAction = null;
                                    };

                                    if (keyboardService.KeyDownStates[KeyValues.MouseMagnifierKey].Value.IsDownOrLockedDown())
                                    {
                                        nextPointSelectionAction = deferIfMagnifyingElseDoNow;
                                    }
                                    else
                                    {
                                        deferIfMagnifyingElseDoNow(repeatFirstClickOrSecondClickAction);
                                    }
                                };
                            }
                            else
                            {
                                //Reset and clean up if we are not continuing to 2nd point
                                SelectionMode = SelectionModes.Key;
                                nextPointSelectionAction = null;
                                ShowCursor = false;
                                if (keyboardService.KeyDownStates[KeyValues.MouseMagnifierKey].Value == KeyDownStates.Down)
                                {
                                    keyboardService.KeyDownStates[KeyValues.MouseMagnifierKey].Value = KeyDownStates.Up; //Release magnifier if down but not locked down
                                }
                            }

                            //Reset and clean up
                            MagnifyAtPoint = null;
                            MagnifiedPointSelectionAction = null;
                        }, finalClickInSeries: false);
                        break;

                    case FunctionKeys.MouseKeyboard:
                        Log.Debug("Changing keyboard to Mouse.");
                        Action backAction;
                        if (keyboardService.KeyDownStates[KeyValues.SimulateKeyStrokesKey].Value.IsDownOrLockedDown()
                            && Settings.Default.SuppressModifierKeysWhenInMouseKeyboard)
                        {
                            var lastLeftShiftValue = keyboardService.KeyDownStates[KeyValues.LeftShiftKey].Value;
                            var lastLeftCtrlValue = keyboardService.KeyDownStates[KeyValues.LeftCtrlKey].Value;
                            var lastLeftWinValue = keyboardService.KeyDownStates[KeyValues.LeftWinKey].Value;
                            var lastLeftAltValue = keyboardService.KeyDownStates[KeyValues.LeftAltKey].Value;
                            keyboardService.KeyDownStates[KeyValues.LeftShiftKey].Value = KeyDownStates.Up;
                            keyboardService.KeyDownStates[KeyValues.LeftCtrlKey].Value = KeyDownStates.Up;
                            keyboardService.KeyDownStates[KeyValues.LeftWinKey].Value = KeyDownStates.Up;
                            keyboardService.KeyDownStates[KeyValues.LeftAltKey].Value = KeyDownStates.Up;
                            backAction = () =>
                            {
                                keyboardService.KeyDownStates[KeyValues.LeftShiftKey].Value = lastLeftShiftValue;
                                keyboardService.KeyDownStates[KeyValues.LeftCtrlKey].Value = lastLeftCtrlValue;
                                keyboardService.KeyDownStates[KeyValues.LeftWinKey].Value = lastLeftWinValue;
                                keyboardService.KeyDownStates[KeyValues.LeftAltKey].Value = lastLeftAltValue;
                                Keyboard = currentKeyboard;
                            };
                        }
                        else
                        {
                            backAction = () => Keyboard = currentKeyboard;
                        }
                        Keyboard = new Mouse(backAction);
                        //Reinstate mouse keyboard docked state (if docked)
                        if (Settings.Default.MainWindowState == WindowStates.Docked)
                        {
                            if (Settings.Default.MouseKeyboardDockSize == DockSizes.Full
                                && Settings.Default.MainWindowDockSize != DockSizes.Full)
                            {
                                mainWindowManipulationService.ResizeDockToFull();
                            }
                            else if (Settings.Default.MouseKeyboardDockSize == DockSizes.Collapsed
                                && Settings.Default.MainWindowDockSize != DockSizes.Collapsed)
                            {
                                mainWindowManipulationService.ResizeDockToCollapsed();
                            }
                        }
                        break;

                    case FunctionKeys.MouseLeftClick:
                        var leftClickPoint = mouseService.GetCursorPosition();
                        Log.DebugFormat("Mouse left click selected at point ({0},{1}).", leftClickPoint.X, leftClickPoint.Y);
                        Action<Point?> performLeftClick = point =>
                        {
                            if (point != null)
                            {
                                mouseService.MoveTo(point.Value);
                            }
                            audioService.PlaySound(Settings.Default.MouseClickSoundFile, Settings.Default.MouseClickSoundVolume);
                            mouseService.LeftButtonClick();
                        };
                        lastMouseActionStateManager.LastMouseAction = () => performLeftClick(leftClickPoint);
                        performLeftClick(null);
                        break;

                    case FunctionKeys.MouseLeftDoubleClick:
                        var leftDoubleClickPoint = mouseService.GetCursorPosition();
                        Log.DebugFormat("Mouse left double click selected at point ({0},{1}).", leftDoubleClickPoint.X, leftDoubleClickPoint.Y);
                        Action<Point?> performLeftDoubleClick = point =>
                        {
                            if (point != null)
                            {
                                mouseService.MoveTo(point.Value);
                            }
                            audioService.PlaySound(Settings.Default.MouseDoubleClickSoundFile, Settings.Default.MouseDoubleClickSoundVolume);
                            mouseService.LeftButtonDoubleClick();
                        };
                        lastMouseActionStateManager.LastMouseAction = () => performLeftDoubleClick(leftDoubleClickPoint);
                        performLeftDoubleClick(null);
                        break;

                    case FunctionKeys.MouseLeftDownUp:
                        var leftDownUpPoint = mouseService.GetCursorPosition();
                        if (keyboardService.KeyDownStates[KeyValues.MouseLeftDownUpKey].Value.IsDownOrLockedDown())
                        {
                            Log.DebugFormat("Pressing mouse left button down at point ({0},{1}).", leftDownUpPoint.X, leftDownUpPoint.Y);
                            audioService.PlaySound(Settings.Default.MouseDownSoundFile, Settings.Default.MouseDownSoundVolume);
                            mouseService.LeftButtonDown();
                            lastMouseActionStateManager.LastMouseAction = null;
                        }
                        else
                        {
                            Log.DebugFormat("Releasing mouse left button at point ({0},{1}).", leftDownUpPoint.X, leftDownUpPoint.Y);
                            audioService.PlaySound(Settings.Default.MouseUpSoundFile, Settings.Default.MouseUpSoundVolume);
                            mouseService.LeftButtonUp();
                            lastMouseActionStateManager.LastMouseAction = null;
                        }
                        break;

                    case FunctionKeys.MouseMiddleClick:
                        var middleClickPoint = mouseService.GetCursorPosition();
                        Log.DebugFormat("Mouse middle click selected at point ({0},{1}).", middleClickPoint.X, middleClickPoint.Y);
                        Action<Point?> performMiddleClick = point =>
                        {
                            if (point != null)
                            {
                                mouseService.MoveTo(point.Value);
                            }
                            audioService.PlaySound(Settings.Default.MouseClickSoundFile, Settings.Default.MouseClickSoundVolume);
                            mouseService.MiddleButtonClick();
                        };
                        lastMouseActionStateManager.LastMouseAction = () => performMiddleClick(middleClickPoint);
                        performMiddleClick(null);
                        break;

                    case FunctionKeys.MouseMiddleDownUp:
                        var middleDownUpPoint = mouseService.GetCursorPosition();
                        if (keyboardService.KeyDownStates[KeyValues.MouseMiddleDownUpKey].Value.IsDownOrLockedDown())
                        {
                            Log.DebugFormat("Pressing mouse middle button down at point ({0},{1}).", middleDownUpPoint.X, middleDownUpPoint.Y);
                            audioService.PlaySound(Settings.Default.MouseDownSoundFile, Settings.Default.MouseDownSoundVolume);
                            mouseService.MiddleButtonDown();
                            lastMouseActionStateManager.LastMouseAction = null;
                        }
                        else
                        {
                            Log.DebugFormat("Releasing mouse middle button at point ({0},{1}).", middleDownUpPoint.X, middleDownUpPoint.Y);
                            audioService.PlaySound(Settings.Default.MouseUpSoundFile, Settings.Default.MouseUpSoundVolume);
                            mouseService.MiddleButtonUp();
                            lastMouseActionStateManager.LastMouseAction = null;
                        }
                        break;

                    case FunctionKeys.MouseMoveAndLeftClick:
                        Log.Debug("Mouse move and left click selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateClick = fp =>
                                {
                                    Log.DebugFormat("Performing mouse left click at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseClickSoundFile, Settings.Default.MouseClickSoundVolume);
                                    mouseService.MoveAndLeftClick(fp, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateClick(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateClick(finalPoint.Value);
                            }

                            ResetAndCleanupAfterMouseAction();
                        });
                        break;

                    case FunctionKeys.MouseMoveAndLeftDoubleClick:
                        Log.Debug("Mouse move and left double click selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateClick = fp =>
                                {
                                    Log.DebugFormat("Performing mouse left double click at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseDoubleClickSoundFile, Settings.Default.MouseDoubleClickSoundVolume);
                                    mouseService.MoveAndLeftDoubleClick(fp, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateClick(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateClick(finalPoint.Value);
                            }
                            
                            ResetAndCleanupAfterMouseAction();
                        });
                        break;

                    case FunctionKeys.MouseMoveAndMiddleClick:
                        Log.Debug("Mouse move and middle click selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateClick = fp =>
                                {
                                    Log.DebugFormat("Performing mouse middle click at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseClickSoundFile, Settings.Default.MouseClickSoundVolume);
                                    mouseService.MoveAndMiddleClick(fp, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateClick(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateClick(finalPoint.Value);
                            }

                            ResetAndCleanupAfterMouseAction();
                        });
                        break;
                        
                    case FunctionKeys.MouseMoveAndRightClick:
                        Log.Debug("Mouse move and right click selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateClick = fp =>
                                {
                                    Log.DebugFormat("Performing mouse right click at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseClickSoundFile, Settings.Default.MouseClickSoundVolume);
                                    mouseService.MoveAndRightClick(fp, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateClick(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateClick(finalPoint.Value);
                            }

                            ResetAndCleanupAfterMouseAction();
                        });
                        break;

                    case FunctionKeys.MouseMoveAmountInPixels:
                        Log.Debug("Progressing MouseMoveAmountInPixels.");
                        switch (Settings.Default.MouseMoveAmountInPixels)
                        {
                            case 1:
                                Settings.Default.MouseMoveAmountInPixels = 5;
                                break;

                            case 5:
                                Settings.Default.MouseMoveAmountInPixels = 10;
                                break;

                            case 10:
                                Settings.Default.MouseMoveAmountInPixels = 25;
                                break;

                            case 25:
                                Settings.Default.MouseMoveAmountInPixels = 50;
                                break;

                            case 50:
                                Settings.Default.MouseMoveAmountInPixels = 100;
                                break;

                            default:
                                Settings.Default.MouseMoveAmountInPixels = 1;
                                break;
                        }
                        break;

                    case FunctionKeys.MouseMoveAndScrollToBottom:
                        Log.Debug("Mouse move and scroll to bottom selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateScrollToBottom = fp =>
                                {
                                    Log.DebugFormat("Performing mouse scroll to bottom at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseScrollSoundFile, Settings.Default.MouseScrollSoundVolume);
                                    mouseService.MoveAndScrollWheelDown(fp, Settings.Default.MouseScrollAmountInClicks, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateScrollToBottom(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateScrollToBottom(finalPoint.Value);
                            }

                            ResetAndCleanupAfterMouseAction();
                        });
                        break;

                    case FunctionKeys.MouseMoveAndScrollToLeft:
                        Log.Debug("Mouse move and scroll to left selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateScrollToLeft = fp =>
                                {
                                    Log.DebugFormat("Performing mouse scroll to left at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseScrollSoundFile, Settings.Default.MouseScrollSoundVolume);
                                    mouseService.MoveAndScrollWheelLeft(fp, Settings.Default.MouseScrollAmountInClicks, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateScrollToLeft(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateScrollToLeft(finalPoint.Value);
                            }

                            ResetAndCleanupAfterMouseAction();
                        });
                        break;

                    case FunctionKeys.MouseMoveAndScrollToRight:
                        Log.Debug("Mouse move and scroll to right selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateScrollToRight = fp =>
                                {
                                    Log.DebugFormat("Performing mouse scroll to right at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseScrollSoundFile, Settings.Default.MouseScrollSoundVolume);
                                    mouseService.MoveAndScrollWheelRight(fp, Settings.Default.MouseScrollAmountInClicks, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateScrollToRight(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateScrollToRight(finalPoint.Value);
                            }

                            ResetAndCleanupAfterMouseAction();
                        });
                        break;

                    case FunctionKeys.MouseMoveAndScrollToTop:
                        Log.Debug("Mouse move and scroll to top selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateScrollToTop = fp =>
                                {
                                    Log.DebugFormat("Performing mouse scroll to top at point ({0},{1}).", fp.X, fp.Y);
                                    audioService.PlaySound(Settings.Default.MouseScrollSoundFile, Settings.Default.MouseScrollSoundVolume);
                                    mouseService.MoveAndScrollWheelUp(fp, Settings.Default.MouseScrollAmountInClicks, true);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateScrollToTop(finalPoint.Value);
                                ShowCursor = false; //Hide cursor popup before performing action as it is possible for it to be performed on the popup
                                simulateScrollToTop(finalPoint.Value);
                            }

                            ResetAndCleanupAfterMouseAction();  
                        });
                        break;

                    case FunctionKeys.MouseMoveTo:
                        Log.Debug("Mouse move to selected.");
                        SetupFinalClickAction(finalPoint =>
                        {
                            if (finalPoint != null)
                            {
                                Action<Point> simulateMoveTo = fp =>
                                {
                                    Log.DebugFormat("Performing mouse move to point ({0},{1}).", fp.X, fp.Y);
                                    mouseService.MoveTo(fp);
                                };
                                lastMouseActionStateManager.LastMouseAction = () => simulateMoveTo(finalPoint.Value);
                                simulateMoveTo(finalPoint.Value);
                            }
                            ResetAndCleanupAfterMouseAction();
                        });
                        break;

                    case FunctionKeys.MouseMoveToBottom:
                        Log.Debug("Mouse move to bottom selected.");
                        Action simulateMoveToBottom = () =>
                        {
                            var cursorPosition = mouseService.GetCursorPosition();
                            var moveToPoint = new Point(cursorPosition.X, cursorPosition.Y + Settings.Default.MouseMoveAmountInPixels);
                            Log.DebugFormat("Performing mouse move to point ({0},{1}).", moveToPoint.X, moveToPoint.Y);
                            mouseService.MoveTo(moveToPoint);
                        };
                        lastMouseActionStateManager.LastMouseAction = simulateMoveToBottom;
                        simulateMoveToBottom();
                        break;

                    case FunctionKeys.MouseMoveToLeft:
                        Log.Debug("Mouse move to left selected.");
                        Action simulateMoveToLeft = () =>
                        {
                            var cursorPosition = mouseService.GetCursorPosition();
                            var moveToPoint = new Point(cursorPosition.X - Settings.Default.MouseMoveAmountInPixels, cursorPosition.Y);
                            Log.DebugFormat("Performing mouse move to point ({0},{1}).", moveToPoint.X, moveToPoint.Y);
                            mouseService.MoveTo(moveToPoint);
                        };
                        lastMouseActionStateManager.LastMouseAction = simulateMoveToLeft;
                        simulateMoveToLeft();
                        break;

                    case FunctionKeys.MouseMoveToRight:
                        Log.Debug("Mouse move to right selected.");
                        Action simulateMoveToRight = () =>
                        {
                            var cursorPosition = mouseService.GetCursorPosition();
                            var moveToPoint = new Point(cursorPosition.X + Settings.Default.MouseMoveAmountInPixels, cursorPosition.Y);
                            Log.DebugFormat("Performing mouse move to point ({0},{1}).", moveToPoint.X, moveToPoint.Y);
                            mouseService.MoveTo(moveToPoint);
                        };
                        lastMouseActionStateManager.LastMouseAction = simulateMoveToRight;
                        simulateMoveToRight();
                        break;

                    case FunctionKeys.MouseMoveToTop:
                        Log.Debug("Mouse move to top selected.");
                        Action simulateMoveToTop = () =>
                        {
                            var cursorPosition = mouseService.GetCursorPosition();
                            var moveToPoint = new Point(cursorPosition.X, cursorPosition.Y - Settings.Default.MouseMoveAmountInPixels);
                            Log.DebugFormat("Performing mouse move to point ({0},{1}).", moveToPoint.X, moveToPoint.Y);
                            mouseService.MoveTo(moveToPoint);
                        };
                        lastMouseActionStateManager.LastMouseAction = simulateMoveToTop;
                        simulateMoveToTop();
                        break;

                    case FunctionKeys.MouseRightClick:
                        var rightClickPoint = mouseService.GetCursorPosition();
                        Log.DebugFormat("Mouse right click selected at point ({0},{1}).", rightClickPoint.X, rightClickPoint.Y);
                        Action<Point?> performRightClick = point =>
                        {
                            if (point != null)
                            {
                                mouseService.MoveTo(point.Value);
                            }
                            audioService.PlaySound(Settings.Default.MouseClickSoundFile, Settings.Default.MouseClickSoundVolume);
                            mouseService.RightButtonClick();
                        };
                        lastMouseActionStateManager.LastMouseAction = () => performRightClick(rightClickPoint);
                        performRightClick(null);
                        break;

                    case FunctionKeys.MouseRightDownUp:
                        var rightDownUpPoint = mouseService.GetCursorPosition();
                        if (keyboardService.KeyDownStates[KeyValues.MouseRightDownUpKey].Value.IsDownOrLockedDown())
                        {
                            Log.DebugFormat("Pressing mouse right button down at point ({0},{1}).", rightDownUpPoint.X, rightDownUpPoint.Y);
                            audioService.PlaySound(Settings.Default.MouseDownSoundFile, Settings.Default.MouseDownSoundVolume);
                            mouseService.RightButtonDown();
                            lastMouseActionStateManager.LastMouseAction = null;
                        }
                        else
                        {
                            Log.DebugFormat("Releasing mouse right button at point ({0},{1}).", rightDownUpPoint.X, rightDownUpPoint.Y);
                            audioService.PlaySound(Settings.Default.MouseUpSoundFile, Settings.Default.MouseUpSoundVolume);
                            mouseService.RightButtonUp();
                            lastMouseActionStateManager.LastMouseAction = null;
                        }
                        break;

                    case FunctionKeys.MoveAndResizeAdjustmentAmount:
                        Log.Debug("Progressing MoveAndResizeAdjustmentAmount.");
                        switch (Settings.Default.MoveAndResizeAdjustmentAmountInPixels)
                        {
                            case 1:
                                Settings.Default.MoveAndResizeAdjustmentAmountInPixels = 5;
                                break;

                            case 5:
                                Settings.Default.MoveAndResizeAdjustmentAmountInPixels = 10;
                                break;

                            case 10:
                                Settings.Default.MoveAndResizeAdjustmentAmountInPixels = 25;
                                break;

                            case 25:
                                Settings.Default.MoveAndResizeAdjustmentAmountInPixels = 50;
                                break;

                            case 50:
                                Settings.Default.MoveAndResizeAdjustmentAmountInPixels = 100;
                                break;

                            default:
                                Settings.Default.MoveAndResizeAdjustmentAmountInPixels = 1;
                                break;
                        }
                        break;

                    case FunctionKeys.MouseScrollAmountInClicks:
                        Log.Debug("Progressing MouseScrollAmountInClicks.");
                        switch (Settings.Default.MouseScrollAmountInClicks)
                        {
                            case 1:
                                Settings.Default.MouseScrollAmountInClicks = 3;
                                break;

                            case 3:
                                Settings.Default.MouseScrollAmountInClicks = 5;
                                break;

                            case 5:
                                Settings.Default.MouseScrollAmountInClicks = 10;
                                break;

                            case 10:
                                Settings.Default.MouseScrollAmountInClicks = 25;
                                break;

                            default:
                                Settings.Default.MouseScrollAmountInClicks = 1;
                                break;
                        }
                        break;

                    case FunctionKeys.MoveToBottom:
                        Log.DebugFormat("Moving to bottom by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.Bottom, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToBottomAndLeft:
                        Log.DebugFormat("Moving to bottom and left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.BottomLeft, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToBottomAndLeftBoundaries:
                        Log.Debug("Moving to bottom and left boundaries.");
                        mainWindowManipulationService.Move(MoveToDirections.BottomLeft, null);
                        break;

                    case FunctionKeys.MoveToBottomAndRight:
                        Log.DebugFormat("Moving to bottom and right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.BottomRight, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToBottomAndRightBoundaries:
                        Log.Debug("Moving to bottom and right boundaries.");
                        mainWindowManipulationService.Move(MoveToDirections.BottomRight, null);
                        break;

                    case FunctionKeys.MoveToBottomBoundary:
                        Log.Debug("Moving to bottom boundary.");
                        mainWindowManipulationService.Move(MoveToDirections.Bottom, null);
                        break;

                    case FunctionKeys.MoveToLeft:
                        Log.DebugFormat("Moving to left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.Left, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToLeftBoundary:
                        Log.Debug("Moving to left boundary.");
                        mainWindowManipulationService.Move(MoveToDirections.Left, null);
                        break;

                    case FunctionKeys.MoveToRight:
                        Log.DebugFormat("Moving to right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.Right, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToRightBoundary:
                        Log.Debug("Moving to right boundary.");
                        mainWindowManipulationService.Move(MoveToDirections.Right, null);
                        break;

                    case FunctionKeys.MoveToTop:
                        Log.DebugFormat("Moving to top by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.Top, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToTopAndLeft:
                        Log.DebugFormat("Moving to top and left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.TopLeft, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToTopAndLeftBoundaries:
                        Log.Debug("Moving to top and left boundaries.");
                        mainWindowManipulationService.Move(MoveToDirections.TopLeft, null);
                        break;

                    case FunctionKeys.MoveToTopAndRight:
                        Log.DebugFormat("Moving to top and right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Move(MoveToDirections.TopRight, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.MoveToTopAndRightBoundaries:
                        Log.Debug("Moving to top and right boundaries.");
                        mainWindowManipulationService.Move(MoveToDirections.TopRight, null);
                        break;

                    case FunctionKeys.MoveToTopBoundary:
                        Log.Debug("Moving to top boundary.");
                        mainWindowManipulationService.Move(MoveToDirections.Top, null);
                        break;
                        
                    case FunctionKeys.NextSuggestions:
                        Log.Debug("Incrementing suggestions page.");

                        if (suggestionService.Suggestions != null
                            && (suggestionService.Suggestions.Count > (suggestionService.SuggestionsPage + 1) * SuggestionService.SuggestionsPerPage))
                        {
                            suggestionService.SuggestionsPage++;
                        }
                        break;

                    case FunctionKeys.NoQuestionResult:
                        HandleYesNoQuestionResult(false);
                        break;

                    case FunctionKeys.NumericAndSymbols1Keyboard:
                        Log.Debug("Changing keyboard to NumericAndSymbols1.");
                        Keyboard = new NumericAndSymbols1();
                        break;

                    case FunctionKeys.NumericAndSymbols2Keyboard:
                        Log.Debug("Changing keyboard to NumericAndSymbols2.");
                        Keyboard = new NumericAndSymbols2();
                        break;

                    case FunctionKeys.NumericAndSymbols3Keyboard:
                        Log.Debug("Changing keyboard to Symbols3.");
                        Keyboard = new NumericAndSymbols3();
                        break;

                    case FunctionKeys.PhysicalKeysKeyboard:
                        Log.Debug("Changing keyboard to PhysicalKeys.");
                        Keyboard = new PhysicalKeys();
                        break;
                        
                    case FunctionKeys.PreviousSuggestions:
                        Log.Debug("Decrementing suggestions page.");

                        if (suggestionService.SuggestionsPage > 0)
                        {
                            suggestionService.SuggestionsPage--;
                        }
                        break;

                    case FunctionKeys.Quit:
                        Log.Debug("Quit key selected.");
                        var keyboardBeforeQuit = Keyboard;
                        Keyboard = new YesNoQuestion("Are you sure you would like to quit?",
                            () =>
                            {
                                Keyboard = new YesNoQuestion("Are you absolutely sure that you'd like to quit?",
                                    () => Application.Current.Shutdown(),
                                    () => { Keyboard = keyboardBeforeQuit; });
                            },
                            () => { Keyboard = keyboardBeforeQuit; });
                        break;

                    case FunctionKeys.RepeatLastMouseAction:
                        if (lastMouseActionStateManager.LastMouseAction != null)
                        {
                            lastMouseActionStateManager.LastMouseAction();
                        }
                        break;

                    case FunctionKeys.ShrinkFromBottom:
                        Log.DebugFormat("Shrinking from bottom by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.Bottom, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ShrinkFromBottomAndLeft:
                        Log.DebugFormat("Shrinking from bottom and left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.BottomLeft, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ShrinkFromBottomAndRight:
                        Log.DebugFormat("Shrinking from bottom and right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.BottomRight, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ShrinkFromLeft:
                        Log.DebugFormat("Shrinking from left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.Left, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ShrinkFromRight:
                        Log.DebugFormat("Shrinking from right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.Right, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ShrinkFromTop:
                        Log.DebugFormat("Shrinking from top by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.Top, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ShrinkFromTopAndLeft:
                        Log.DebugFormat("Shrinking from top and left by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.TopLeft, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.ShrinkFromTopAndRight:
                        Log.DebugFormat("Shrinking from top and right by {0}px.", Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        mainWindowManipulationService.Shrink(ShrinkFromDirections.TopRight, Settings.Default.MoveAndResizeAdjustmentAmountInPixels);
                        break;

                    case FunctionKeys.SizeAndPositionKeyboard:
                        Log.Debug("Changing keyboard to Size & Position.");
                        Keyboard = new SizeAndPosition(() => Keyboard = currentKeyboard);
                        break;

                    case FunctionKeys.Speak:
                        var speechStarted = audioService.SpeakNewOrInterruptCurrentSpeech(
                            textOutputService.Text,
                            () => { KeyboardService.KeyDownStates[KeyValues.SpeakKey].Value = KeyDownStates.Up; },
                            Settings.Default.SpeechVolume,
                            Settings.Default.SpeechRate,
                            Settings.Default.SpeechVoice);
                        KeyboardService.KeyDownStates[KeyValues.SpeakKey].Value = speechStarted ? KeyDownStates.Down : KeyDownStates.Up;
                        break;

                    case FunctionKeys.YesQuestionResult:
                        HandleYesNoQuestionResult(true);
                        break;
                }

                textOutputService.ProcessFunctionKey(singleKeyValue.FunctionKey.Value);
            }
        }

        private void SetupFinalClickAction(Action<Point?> finalClickAction, bool finalClickInSeries = true)
        {
            nextPointSelectionAction = nextPoint =>
            {
                if (keyboardService.KeyDownStates[KeyValues.MouseMagnifierKey].Value.IsDownOrLockedDown())
                {
                    ShowCursor = false; //Ensure cursor is not showing when MagnifyAtPoint is set because...
                    //1.This triggers a screen capture, which shouldn't have the cursor in it.
                    //2.Last popup open stays on top (I know the VM in MVVM shouldn't care about this, so pretend it's all reason 1).
                    MagnifiedPointSelectionAction = finalClickAction;
                    MagnifyAtPoint = nextPoint;
                    ShowCursor = true;
                }
                else
                {
                    finalClickAction(nextPoint);
                }

                if (finalClickInSeries)
                {
                    nextPointSelectionAction = null;
                }
            };

            SelectionMode = SelectionModes.Point;
            ShowCursor = true;
        }

        private void ResetAndCleanupAfterMouseAction()
        {
            SelectionMode = SelectionModes.Key;
            nextPointSelectionAction = null;
            ShowCursor = false;
            MagnifyAtPoint = null;
            MagnifiedPointSelectionAction = null;
            if (keyboardService.KeyDownStates[KeyValues.MouseMagnifierKey].Value == KeyDownStates.Down)
            {
                keyboardService.KeyDownStates[KeyValues.MouseMagnifierKey].Value = KeyDownStates.Up; //Release magnifier if down but not locked down
            }
        }

        public void HandleServiceError(object sender, Exception exception)
        {
            Log.Error("Error event received from service. Raising ErrorNotificationRequest and playing ErrorSoundFile (from settings)", exception);

            inputService.RequestSuspend();
            audioService.PlaySound(Settings.Default.ErrorSoundFile, Settings.Default.ErrorSoundVolume);
            RaiseToastNotification("Uh-oh!", exception.Message, NotificationTypes.Error, () => inputService.RequestResume());
        }
    }
}
