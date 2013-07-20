//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.SkeletonBasics
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using Microsoft.Kinect;
    using System.Globalization;
    using FORMS = System.Windows.Forms;
    using System.Windows.Input;
    using System.Runtime.InteropServices;
    using Microsoft.Speech.AudioFormat;
    using Microsoft.Speech.Recognition;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Width of output drawing
        /// </summary>
        private const float RenderWidth = 640.0f;

        /// <summary>
        /// Height of our output drawing
        /// </summary>
        private const float RenderHeight = 480.0f;

        /// <summary>
        /// Thickness of drawn joint lines
        /// </summary>
        private const double JointThickness = 6;

        /// <summary>
        /// Thickness of body center ellipse
        /// </summary>
        private const double BodyCenterThickness = 10;

        /// <summary>
        /// Thickness of clip edge rectangles
        /// </summary>
        private const double ClipBoundsThickness = 10;

        /// <summary>
        /// Brush used to draw skeleton center point
        /// </summary>
        private readonly Brush centerPointBrush = Brushes.Blue;

        /// <summary>
        /// Brush used for drawing joints that are currently tracked
        /// </summary>
        private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 0, 0, 200));

        /// <summary>
        /// Brush used for drawing joints that are currently inferred
        /// </summary>        
        private readonly Brush inferredJointBrush = Brushes.Yellow;

        /// <summary>
        /// Pen used for drawing bones that are currently tracked
        /// </summary>
        private readonly Pen trackedBonePen = new Pen(Brushes.Black, 6);

        /// <summary>
        /// Pen used for drawing bones that are currently inferred
        /// </summary>        
        private readonly Pen inferredBonePen = new Pen(Brushes.Orange, 1);

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Drawing group for skeleton rendering output
        /// </summary>
        private DrawingGroup drawingGroup;

        /// <summary>
        /// Drawing image that we will display
        /// </summary>
        private DrawingImage imageSource;

        const UInt32 WM_KEYDOWN = 0x0100;
        const UInt32 WM_KEYUP = 0x0101;
        const int VK_E = 0x45; // UP (second life)
        const int VK_C = 0x43; // DOWN (second life)
        const int VK_F = 0x46; // FLY (second life)
        const int VK_ENTER = 0x0D; // Enter message (second life)
        const int VK_ESC = 0x1B; // Exit message (second life)
        const int VK_ARROW_LEFT = 0X25;
        const int VK_ARROW_RIGHT = 0x27;
        const int VK_ARROW_UP = 0x26;


        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, UInt32 Msg, int wParam, int lParam);

        private bool flying;
        private int currentSkeleton;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            flying = false;
            currentSkeleton = -1; // No skeleton tracked at the beginning
            InitializeComponent();
        }

        /// <summary>
        /// Draws indicators to show which edges are clipping skeleton data
        /// </summary>
        /// <param name="skeleton">skeleton to draw clipping information for</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private static void RenderClippedEdges(Skeleton skeleton, DrawingContext drawingContext)
        {
            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Bottom))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, RenderHeight - ClipBoundsThickness, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Top))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Left))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, ClipBoundsThickness, RenderHeight));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Right))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(RenderWidth - ClipBoundsThickness, 0, ClipBoundsThickness, RenderHeight));
            }
        }

        /// <summary>
        /// Execute startup tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            // Create the drawing group we'll use for drawing
            this.drawingGroup = new DrawingGroup();

            // Create an image source that we can use in our image control
            this.imageSource = new DrawingImage(this.drawingGroup);

            // Display the drawing using our image control
            Image.Source = this.imageSource;

            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the skeleton stream to receive skeleton frames
                this.sensor.SkeletonStream.Enable();

                // Add an event handler to be called whenever there is new color frame data
                this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;

                // Start the sensor!
                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }

            if (null == this.sensor)
            {
                this.statusBarText.Text = Properties.Resources.NoKinectReady;
            }

        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }
        }

        /// <summary>
        /// Event handler for Kinect sensor's SkeletonFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons = new Skeleton[0];

            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(skeletons);
                }
            }

            using (DrawingContext dc = this.drawingGroup.Open())
            {
                // Draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.White, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

                if (skeletons.Length != 0)
                {
                    // Check if current skeleton is tracked
                    bool currentSkeletonTracked = false;
                    if (currentSkeleton != -1)
                    {
                        foreach (Skeleton skel in skeletons)
                        {
                            if (skel.TrackingId == currentSkeleton)
                            {
                                currentSkeletonTracked = true;
                                break;
                            }
                        }
                    }

                    // Re-enable autodetect
                    if (!currentSkeletonTracked && currentSkeleton != -1)
                    {
                        sensor.SkeletonStream.AppChoosesSkeletons = false;
                        currentSkeleton = -1;
                    }
                    
                    foreach (Skeleton skel in skeletons)
                    {
                        RenderClippedEdges(skel, dc);

                        if (skel.TrackingState == SkeletonTrackingState.Tracked && currentSkeleton == -1)
                        {
                            sensor.SkeletonStream.AppChoosesSkeletons = true;
                            sensor.SkeletonStream.ChooseSkeletons(skel.TrackingId);
                            currentSkeleton = skel.TrackingId;
                        }

                        if (skel.TrackingState == SkeletonTrackingState.Tracked && skel.TrackingId == currentSkeleton)
                        {
                            this.DrawBonesAndJoints(skel, dc);
                            this.ParseToCommands(skel, dc);
                        }
                        else if (skel.TrackingState == SkeletonTrackingState.PositionOnly) // When skeleton cannot be tracker ( > 2 ppl)
                        {
                            dc.DrawEllipse(
                            this.centerPointBrush,
                            null,
                            this.SkeletonPointToScreen(skel.Position),
                            BodyCenterThickness,
                            BodyCenterThickness);
                        }
                    }
                }

                // prevent drawing outside of our render area
                this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
            }
        }

        
        // DOING 
        private void ParseToCommands(Skeleton skeleton, DrawingContext drawingContext)
        {
            bool staying = isStayingState(skeleton, drawingContext);
            bool startFlying = isStartFlying(skeleton, drawingContext);
            bool stopFlying = isStopFlying(skeleton, drawingContext);
            bool goUp = isGoUp(skeleton, drawingContext);
            bool goDown = isGoDown(skeleton, drawingContext);
            bool goForward = isGoForward(skeleton, drawingContext);
            bool goLeft = isGoLeft(skeleton, drawingContext);
            bool goRight = isGoRight(skeleton, drawingContext);

            if (!staying)
            {
                if (startFlying && !flying)
                {
                    pressKey(VK_F);
                    releaseKey(VK_F);
                    flying = true;
                }
                else if (stopFlying && flying)
                {
                    pressKey(VK_F);
                    releaseKey(VK_F);
                    flying = false;
                }
                else if (goUp)
                {
                    pressKey(VK_E);
                    releaseKey(VK_E);
                }
                else if (goDown)
                {
                    pressKey(VK_C);
                    releaseKey(VK_C);
                }
                else if (goForward)
                {
                    pressKey(VK_ARROW_UP);
                    releaseKey(VK_ARROW_UP);
                }
                else if (goLeft)
                {
                  
                        pressKey(VK_ARROW_LEFT);
                        releaseKey(VK_ARROW_LEFT);
                        pressKey(VK_ARROW_UP);
                        releaseKey(VK_ARROW_UP);
                }
                else if (goRight)
                {
                   
                        pressKey(VK_ARROW_RIGHT);
                        releaseKey(VK_ARROW_RIGHT);
                        pressKey(VK_ARROW_UP);
                        releaseKey(VK_ARROW_UP);
                }
            }
        }

        private const float ratioWristShoulder = 5.0f;
        private bool isStayingState(Skeleton skeleton, DrawingContext drawingContext)
        {
            Joint hipCenter = skeleton.Joints[JointType.HipCenter];
            Joint wristLeft = skeleton.Joints[JointType.WristLeft];
            Joint wristRight = skeleton.Joints[JointType.WristRight];

            if (hipCenter.Position.Y > wristLeft.Position.Y &&
                hipCenter.Position.Y > wristRight.Position.Y)
            {

                printText("STAY", drawingContext);
                return true;
            }
            return false;
        }

        private bool isStartFlying(Skeleton skeleton, DrawingContext drawingContext)
        {
            Joint elbowLeft = skeleton.Joints[JointType.ElbowLeft];
            Joint elbowRight = skeleton.Joints[JointType.ElbowRight];
            Joint head = skeleton.Joints[JointType.Head];

            if (head.Position.Y < elbowLeft.Position.Y &&
                head.Position.Y < elbowRight.Position.Y)
            {
                printText("START FLYING", drawingContext);
                return true;
            }
            return false;
        }

        private bool isStopFlying(Skeleton skeleton, DrawingContext drawingContext)
        {
            Joint wristLeft = skeleton.Joints[JointType.WristLeft];
            Joint wristRight = skeleton.Joints[JointType.WristRight];
            Joint head = skeleton.Joints[JointType.Head];
            Joint spine = skeleton.Joints[JointType.Spine];
            Joint shoulderRight = skeleton.Joints[JointType.ShoulderRight];
            Joint shoulderLeft = skeleton.Joints[JointType.ShoulderLeft];

            float distanceWristShoulderLeft = Math.Abs(wristLeft.Position.X - shoulderLeft.Position.X);
            float distanceShoulders = Math.Abs(shoulderLeft.Position.X - shoulderRight.Position.X);
            if (wristLeft.Position.Y > spine.Position.Y &&
                wristLeft.Position.Y < head.Position.Y &&
                (wristRight.Position.Y < spine.Position.Y ||
                    wristRight.Position.Y > head.Position.Y) &&
                (distanceWristShoulderLeft * ratioWristShoulder) < distanceShoulders)
            {
                printText("STOP FLYING", drawingContext);
                return true;
            }

            return false;
        }

        private bool isGoUp(Skeleton skeleton, DrawingContext drawingContext)
        {
            Joint wristLeft = skeleton.Joints[JointType.WristLeft];
            Joint wristRight = skeleton.Joints[JointType.WristRight];
            Joint head = skeleton.Joints[JointType.Head];

            if (head.Position.Y < wristLeft.Position.Y &&
                head.Position.Y < wristRight.Position.Y)
            {
                printText("GO UP", drawingContext);
                return true;
            }
            return false;
        }

        private bool isGoDown(Skeleton skeleton, DrawingContext drawingContext)
        {
            Joint wristLeft = skeleton.Joints[JointType.WristLeft];
            Joint wristRight = skeleton.Joints[JointType.WristRight];
            Joint shoulderCenter = skeleton.Joints[JointType.ShoulderCenter];
            Joint hipCenter = skeleton.Joints[JointType.HipCenter];

            // 1/2 distance between shoulderCenter and hipCenter
            float y_fration = (float) ((shoulderCenter.Position.Y - hipCenter.Position.Y) / 2.0); // not working
            y_fration = shoulderCenter.Position.Y - y_fration;
            
            if (y_fration > wristLeft.Position.Y &&
                y_fration > wristRight.Position.Y &&
                hipCenter.Position.Y < wristLeft.Position.Y &&
                hipCenter.Position.Y < wristRight.Position.Y)
            {
                printText("GO DOWN", drawingContext);
                return true;
            }
            return false;
        }

        private bool isGoForward(Skeleton skeleton, DrawingContext drawingContext)
        {
            Joint wristLeft = skeleton.Joints[JointType.WristLeft];
            Joint wristRight = skeleton.Joints[JointType.WristRight];
            Joint head = skeleton.Joints[JointType.Head];
            Joint spine = skeleton.Joints[JointType.Spine];
            Joint shoulderRight = skeleton.Joints[JointType.ShoulderRight];
            Joint shoulderLeft = skeleton.Joints[JointType.ShoulderLeft];

            float distanceWristShoulderRight = Math.Abs(wristRight.Position.X - shoulderRight.Position.X);
            float distanceShoulders = Math.Abs(shoulderLeft.Position.X - shoulderRight.Position.X);
            if (wristRight.Position.Y > spine.Position.Y &&
                wristRight.Position.Y < head.Position.Y &&
                (wristLeft.Position.Y < spine.Position.Y ||
                    wristLeft.Position.Y > head.Position.Y) &&
                (distanceWristShoulderRight * ratioWristShoulder) < distanceShoulders)
            {
                printText("GO FORWARD", drawingContext);
                return true;
            }
            return false;
        }

        private bool isGoLeft(Skeleton skeleton, DrawingContext drawingContext)
        {

            Joint wristLeft = skeleton.Joints[JointType.WristLeft];
            Joint wristRight = skeleton.Joints[JointType.WristRight];
            Joint head = skeleton.Joints[JointType.Head];
            Joint spine = skeleton.Joints[JointType.Spine];
            Joint shoulderRight = skeleton.Joints[JointType.ShoulderRight];
            Joint shoulderLeft = skeleton.Joints[JointType.ShoulderLeft];

            float distanceWristShoulderLeft = Math.Abs(wristLeft.Position.X - shoulderLeft.Position.X);
            float distanceShoulders = Math.Abs(shoulderLeft.Position.X - shoulderRight.Position.X);
            if ((distanceWristShoulderLeft * ratioWristShoulder) > distanceShoulders &&
                 (wristRight.Position.Y < spine.Position.Y ||
                    wristRight.Position.Y > head.Position.Y) &&
                wristLeft.Position.Y > spine.Position.Y &&
                wristLeft.Position.Y < head.Position.Y) 
                {
                    printText("GO LEFT", drawingContext);
                    return true;
                }

            return false;
        }

        private bool isGoRight(Skeleton skeleton, DrawingContext drawingContext)
        {
            Joint wristLeft = skeleton.Joints[JointType.WristLeft];
            Joint wristRight = skeleton.Joints[JointType.WristRight];
            Joint head = skeleton.Joints[JointType.Head];
            Joint spine = skeleton.Joints[JointType.Spine];
            Joint shoulderRight = skeleton.Joints[JointType.ShoulderRight];
            Joint shoulderLeft = skeleton.Joints[JointType.ShoulderLeft];

            float distanceWristShoulderRight = Math.Abs(wristRight.Position.X - shoulderRight.Position.X);
            float distanceShoulders = Math.Abs(shoulderLeft.Position.X - shoulderRight.Position.X);
            if ((distanceWristShoulderRight * ratioWristShoulder) > distanceShoulders &&
                 (wristLeft.Position.Y < spine.Position.Y ||
                    wristLeft.Position.Y > head.Position.Y) &&
                wristRight.Position.Y > spine.Position.Y &&
                wristRight.Position.Y < head.Position.Y)
            {
                printText("GO RIGHT", drawingContext);
                return true;
            }
            return false;
        }


        private void pressKey(int key)
        {
            Process[] processes = Process.GetProcessesByName("SecondLife");
            foreach (Process proc in processes)
                PostMessage(proc.MainWindowHandle, WM_KEYDOWN, key, 0);
        }

        private void releaseKey(int key)
        {
            Process[] processes = Process.GetProcessesByName("SecondLife");
            foreach (Process proc in processes)
                PostMessage(proc.MainWindowHandle, WM_KEYUP, key, 0);
        }

        private void printText(String text, DrawingContext drawingContext)
        {
            var formattedText = new FormattedText(text,
                  CultureInfo.CurrentCulture,
                  FlowDirection.LeftToRight,
                  new Typeface("Calibri"),
                  30,
                  Brushes.Green);
            drawingContext.DrawText(formattedText, new Point(100, 100));
        }

        // Code from MICROSOFT - Draws skeleton
        /// <summary>
        /// Draws a skeleton's bones and joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext)
        {
            // Render Torso
            this.DrawBone(skeleton, drawingContext, JointType.Head, JointType.ShoulderCenter);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderRight);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.Spine);
            this.DrawBone(skeleton, drawingContext, JointType.Spine, JointType.HipCenter);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipLeft);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipRight);

            // Left Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
            this.DrawBone(skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);

            // Right Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
            this.DrawBone(skeleton, drawingContext, JointType.WristRight, JointType.HandRight);

            // Left Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipLeft, JointType.KneeLeft);
            this.DrawBone(skeleton, drawingContext, JointType.KneeLeft, JointType.AnkleLeft);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleLeft, JointType.FootLeft);

            // Right Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipRight, JointType.KneeRight);
            this.DrawBone(skeleton, drawingContext, JointType.KneeRight, JointType.AnkleRight);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleRight, JointType.FootRight);

            // Render Joints
            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), JointThickness, JointThickness);
                }
            }
        }

        /// <summary>
        /// Maps a SkeletonPoint to lie within our render space and converts to Point
        /// </summary>
        /// <param name="skelpoint">point to map</param>
        /// <returns>mapped point</returns>
        private Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        /// <summary>
        /// Draws a bone line between two joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw bones from</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="jointType0">joint to start drawing from</param>
        /// <param name="jointType1">joint to end drawing at</param>
        private void DrawBone(Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
        {
            Joint joint0 = skeleton.Joints[jointType0];
            Joint joint1 = skeleton.Joints[jointType1];

            // If we can't find either of these joints, exit
            if (joint0.TrackingState == JointTrackingState.NotTracked ||
                joint1.TrackingState == JointTrackingState.NotTracked)
            {
                return;
            }

            // Don't draw if both points are inferred
            if (joint0.TrackingState == JointTrackingState.Inferred &&
                joint1.TrackingState == JointTrackingState.Inferred)
            {
                return;
            }

            // We assume all drawn bones are inferred unless BOTH joints are tracked
            Pen drawPen = this.inferredBonePen;
            if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
            {
                drawPen = this.trackedBonePen;
            }

            drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));
        }

        /// <summary>
        /// Handles the checking or unchecking of the seated mode combo box
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void CheckBoxSeatedModeChanged(object sender, RoutedEventArgs e)
        {
            if (null != this.sensor)
            {
                if (this.checkBoxSeatedMode.IsChecked.GetValueOrDefault())
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                }
                else
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
                }
            }
        }
    }

    
}