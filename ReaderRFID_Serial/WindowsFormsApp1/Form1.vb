﻿Imports System
Imports System.Collections
Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.ComponentModel
Imports System.Data
Imports System.Drawing
Imports System.IO.Ports
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

Namespace WindowsFormsApp1
	Partial Public Class Form1
		Inherits Form

		Public Sub New()
			InitializeComponent()
		End Sub
		Private Shared ReadOnly FLAG_IN_INVENTORY As Integer = BitVector32.CreateMask()
		Private Shared ReadOnly FLAG_STOP_INVENTORY As Integer = BitVector32.CreateMask(FLAG_IN_INVENTORY)

		Public ReadOnly Property Reader() As Reader
			Get
				Return m_reader
			End Get
		End Property

		' 标识集合
		Private m_flags As New BitVector32()

		' 停止盘点等待的时间
		Private Shared ReadOnly s_usStopInventoryTimeout As UShort = 10000
		' 盘点类型
		Private m_btInvType As Byte = 0
		' 盘点类型参数
		Private m_uiInvParam As UInteger = 0

		' 是否正在显示标签数据，如果正在显示则为1，否则为0
		Private m_iShowingTag As Integer = 0
		' 是否只更新列，如果是则为1 ，如果不是则为0；表示有没有更新的EPC号码
		Private m_iShowRow As Integer = 0

		''' <summary>
		''' 每页显示的行数
		''' </summary>
		Private m_iPageLines As Integer = 30
		''' <summary>
		''' 当前显示的行
		''' </summary>
		Private m_iPageIndex As Integer = 0

		' 标签数据，用于查找相同的标签项，只是用来查找是否有相同卡号
		Private m_tags As New Dictionary(Of Byte(), ShowTagItem)(1024, New TagCodeCompare())
		' 标签数据，用于标签按接收次序排序
		Private m_tags2 As New List(Of ShowTagItem)(1024)
		' 标签盘点响应总个数
		Private m_iInvTagCount As Integer = 0
		' 标签盘点总时间
		Private m_iInvTimeMs As Integer = 1
		' 开始盘点的时间
		Private m_iInvStartTick As Integer = 0
		' 盘点线程
		Private m_thInventory As Thread = Nothing
'INSTANT VB TODO TASK: There is no VB equivalent to 'volatile':
'ORIGINAL LINE: volatile bool m_bClosed = false;
		Private m_bClosed As Boolean = False
		Private m_reader As New Reader()
		Private devicepara As New Devicepara()


		Protected Property InInventory() As Boolean
			Get
				Return m_flags(FLAG_IN_INVENTORY)
			End Get
			Set(ByVal value As Boolean)
				m_flags(FLAG_IN_INVENTORY) = value
			End Set
		End Property

		Public Property StopInventory() As Boolean
			Get
				Return m_flags(FLAG_STOP_INVENTORY)
			End Get
			Set(ByVal value As Boolean)
				m_flags(FLAG_STOP_INVENTORY) = value
			End Set
		End Property

		Public ReadOnly Property IsClosed() As Boolean
			Get
				Return m_bClosed
			End Get
		End Property


		Private Delegate Sub WriteLogHandler(ByVal type As MessageType, ByVal msg As String, ByVal ex As Exception)
		Public Sub WriteLog(ByVal type As MessageType, ByVal msg As String, ByVal ex As Exception)
			Try
				If Me.InvokeRequired Then
					Me.BeginInvoke(New WriteLogHandler(AddressOf WriteLog), type, msg, ex)
					Return
				End If

				Dim sb As New StringBuilder(128)
				sb.Append(DateTime.Now)
				sb.Append(", ")
				Select Case type
					Case MessageType.Info
						sb.Append("info：")
					Case MessageType.Warning
						sb.Append("warning：")
					Case MessageType.Error
						sb.Append("error：")
				End Select
				If msg.Length > 0 Then
					sb.Append(msg)
				End If
				If ex IsNot Nothing Then
					sb.Append(ex.Message)
				End If
				sb.Append(vbCrLf)

				Dim msg2 As String = sb.ToString()
				stdSerialData.AppendText(msg2)
				stdSerialData.SelectionLength = 0
				stdSerialData.SelectionStart = stdSerialData.TextLength
				stdSerialData.ScrollToCaret()
			Catch
			End Try
		End Sub

		Private Sub btnSportOpen_Click(ByVal sender As Object, ByVal e As EventArgs) Handles btnSportOpen.Click
			Try
				Dim port As String = cmbComPort.Text.Trim()
				If port.Length = 0 Then
					MessageBox.Show(Me, "Failed to open the serial port, please enter the serial port number", Me.Text)
					Return
				End If
				If m_reader.IsOpened Then
					MessageBox.Show(Me, "The reader is already open, please close the reader first", Me.Text)
					Return
				End If
				'serialport.DataReceived += serialport_DataReceived;
				m_reader.Open(port, CByte(cmbComBaud.SelectedIndex))

				WriteLog(MessageType.Info, "The reader is opened successfully, the serial port number：" & port, Nothing)
				'WriteLog(MessageType.Info, "阅读器打开成功，串口号：" + port, null);
				cmbComPort.Enabled = False
				btnSportOpen.Enabled = False
				groupNet.Enabled = False
			Catch ex As Exception
				Try
					'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
					Dim reader_Conflict As Reader = Me.Reader
					If reader_Conflict.IsOpened Then
						reader_Conflict.Close()
					End If
				Catch
				End Try

				WriteLog(MessageType.Error, "Reader failed to open", ex)
				cmbComPort.Enabled = True
				btnSportOpen.Enabled = True
				MessageBox.Show(Me, "Reader failed to open：" & ex.Message, Me.Text)
			End Try
			InitReader()
		End Sub

		Private Sub InitReader()
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict.IsOpened Then
					cmbComPort.Enabled = True
					devicepara = reader_Conflict.GetDevicePara()
					'cmbTxPower.Text = devicepara.Power.ToString() ' 获取功率
					If devicepara.Workmode > 1 Then
						cmbWorkmode.SelectedIndex = 0 'answer mode
					Else
						cmbWorkmode.SelectedIndex = devicepara.Workmode ' active mode
					End If
					'cmbRegion.SelectedIndex = devicepara.Region
					'btnGetOutInterface.PerformClick()
					'btnGetAllPara.PerformClick();                                         // 需要enbable后才能虚拟click按键
					'                                                                      //WriteLog(MessageType.Info,"Get device parameters", null);
				End If
			Catch ex As Exception

				btnSportClose.PerformClick()
				btnDisConnect.PerformClick()

				WriteLog(MessageType.Error, " Failed to get Power", ex)
				MessageBox.Show(Me, "Failed to get power：" & ex.Message, Me.Text)
				Return
			End Try

		End Sub

		Private Sub btnConnect_Click(ByVal sender As Object, ByVal e As EventArgs)
			Try
				Dim ip As String = txbIPAddr.Text.Trim()
				If ip.Length = 0 Then
					MessageBox.Show(Me, "Connect Failed，please input IP address", Me.Text)
					Return
				End If
				Dim ip2 As System.Net.IPAddress = Nothing
				If Not System.Net.IPAddress.TryParse(ip, ip2) Then
					MessageBox.Show(Me, "Error IPV4 Address", Me.Text)
					Return
				End If
				Dim port As String = txbPort.Text.Trim()
				If port.Length = 0 Then
					MessageBox.Show(Me, "Please input port", Me.Text)
					Return
				End If
				Dim port2 As UShort = Nothing
				If Not UShort.TryParse(port, port2) OrElse port2 = 0 Then
					MessageBox.Show(Me, "port num must 0~65535", Me.Text)
					Return
				End If
				If m_reader.IsOpened Then
					MessageBox.Show(Me, "reader is already opened ，please close first", Me.Text)
					Return
				End If

				m_reader.Open(ip, port2, 3000, True) ' 3000ms等待时间

				WriteLog(MessageType.Info, "reader open success，IP address：" & ip2.ToString() & "，port：" & port2.ToString(), Nothing)

				txbIPAddr.Enabled = False
				txbPort.Enabled = False
				'tnSportOpen.Enabled = False
				btnConnect.Enabled = False
				'tnSportClose.Enabled = False

				'Settings.Default.IPAddr = ip2.ToString();
				'Settings.Default.Port = port2.ToString();
				'Settings.Default.Save();

			Catch ex As Exception
				Try
					'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
					Dim reader_Conflict As Reader = Me.Reader
					If reader_Conflict.IsOpened Then
						reader_Conflict.Close()
					End If
				Catch
				End Try

				WriteLog(MessageType.Error, "open reader fail", ex)
				'mbComPort.Enabled = True
				'cmbComBaud.Enabled = True
				txbIPAddr.Enabled = True
				txbPort.Enabled = True
				'btnSportOpen.Enabled = True
				btnConnect.Enabled = True
				'btnSportClose.Enabled = True
				MessageBox.Show(Me, "open reader fail：" & ex.Message, Me.Text)
			End Try
			InitReader()
		End Sub

		Private Sub btnDisConnect_Click(ByVal sender As Object, ByVal e As EventArgs)
			Try
				WriteLog(MessageType.Info, "Reader Disconnected", Nothing)
				'cmbComPort.Enabled = True
				'cmbComBaud.Enabled = True
				txbIPAddr.Enabled = True
				txbPort.Enabled = True
				'btnSportOpen.Enabled = True
				btnConnect.Enabled = True
				'btnSportClose.Enabled = True

				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Return
				End If

				reader_Conflict.Close()
			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub

		Private Sub btnSetTxPower_Click(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub btngetTxPower_Click(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub btnSetWorkMode_Click(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub btnGetWorkMode_Click(ByVal sender As Object, ByVal e As EventArgs)

		End Sub



		Private Sub CloseInventoryThread()
			Try
				StopInventory = True
				If Not m_thInventory.Join(4000) Then
					m_thInventory.Abort()
				End If
			Catch
			End Try
		End Sub

		Private Sub OnInventoryEnd()
			InInventory = False
			StopInventory = True
			btnInventory.Enabled = True
			WriteLog(MessageType.Info, "Inventory completed", Nothing)
		End Sub

		Private Sub DoStopInventory()
			Try
				InInventory = False
				StopInventory = True
				btnInventory.Enabled = True
				Try
					'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
					Dim reader_Conflict As Reader = Me.Reader
					If reader_Conflict IsNot Nothing Then
						reader_Conflict.InventoryStop(s_usStopInventoryTimeout)
					End If
				Catch e1 As Exception
				End Try
			Catch
			End Try
			Try
				Me.BeginInvoke(New ThreadStart(AddressOf OnInventoryEnd))
			Catch
			End Try
		End Sub

		Private Sub UpdatePageIndex()
			Dim count As Integer = m_tags.Count
			Dim pageCount As Integer = count \ m_iPageLines
			If count = 0 Then
				m_iPageIndex = 0
				Return
			End If

			If m_iPageIndex < 0 Then
				m_iPageIndex = 0
			ElseIf m_iPageIndex > count Then
				m_iPageIndex = count - 1
			End If
		End Sub

		Private Sub ShowTag()
			Try
				If Me.InvokeRequired Then
					' 如果当前已经正在显示标签，则不用再次调用ShowTag
					If Interlocked.Exchange(m_iShowingTag, 1) = 1 Then
						Return
					End If

					' 显示标签
					Me.BeginInvoke(New ThreadStart(AddressOf ShowTag))
					Return
				End If

				Dim lvItems() As ListViewItem
				Dim totalCount As Integer
				Dim tagCount As Integer
				Dim pageIndex As Integer
				Dim pageLines As Integer
				Dim totalTimeMs As Integer
				SyncLock m_tags
					UpdatePageIndex()
					If m_iPageLines = 0 Then
						Interlocked.Exchange(m_iShowingTag, 0)
						Return
					End If
					tagCount = m_tags2.Count
					totalCount = m_iInvTagCount
					totalTimeMs = m_iInvTimeMs
					pageIndex = m_iPageIndex
					pageLines = m_iPageLines

					Dim index As Integer = m_iPageIndex
					Dim count As Integer = tagCount - m_iPageIndex
					lvItems = New ListViewItem(If(m_iPageLines > count, count, m_iPageLines) - 1) {}
					Dim i As Integer = 0
					Do While i < m_iPageLines AndAlso index < tagCount
						Dim sitem As ShowTagItem = m_tags2(index)
						Dim lvItem As New ListViewItem((index + 1).ToString())
						lvItem.Tag = sitem
						lvItem.SubItems.Add(Util.HexArrayToString(sitem.Code))
						lvItem.SubItems.Add(sitem.LEN.ToString()) '显示长度不显示信道
						lvItem.SubItems.Add(sitem.CountsToString())
						lvItem.SubItems.Add((sitem.Rssi \ 10).ToString())
						lvItem.SubItems.Add(sitem.Channel.ToString())
						lvItems(i) = lvItem
						i += 1
						index += 1
					Loop
				End SyncLock

				If lvItems.Length <> lsvTagsActive.Items.Count Then
					lsvTagsActive.Items.Clear()
					lsvTagsActive.Items.AddRange(lvItems)
				Else
					'lsvTags.BeginUpdate();

					For i As Integer = 0 To lvItems.Length - 1 '只对第三列进行刷新
						lsvTagsActive.Items(i).SubItems(2).Text = lvItems(i).SubItems(2).Text ' count
						lsvTagsActive.Items(i).SubItems(3).Text = lvItems(i).SubItems(3).Text ' Rssi
						lsvTagsActive.Items(i).SubItems(4).Text = lvItems(i).SubItems(4).Text ' channel

					Next i
					'lsvTags.EndUpdate();

				End If
				Interlocked.Exchange(m_iShowingTag, 0)
			Catch ex As Exception
				Try
					Interlocked.Exchange(m_iShowingTag, 0)
				Catch
				End Try
				WriteLog(MessageType.Error, "An error occurred while displaying the label：", ex)
			End Try
		End Sub



		''' <summary>
		''' 盘点线程主函数
		''' </summary>
		Private Sub InventoryThread()
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					DoStopInventory()
					Return
				End If

				SyncLock m_tags
					m_tags.Clear()
					m_tags2.Clear()
				End SyncLock

				ShowTag()

				m_iInvTagCount = 0
				m_iInvStartTick = Environment.TickCount

				Do While Not StopInventory
					Dim item As TagItem '接收标签数据
					Try
						item = reader_Conflict.GetTagUii(1000)

					Catch ex As ReaderException
						If ex.ErrorCode = ReaderException.ERROR_CMD_COMM_TIMEOUT OrElse ex.ErrorCode = ReaderException.ERROR_CMD_RESP_FORMAT_ERROR Then
							If reader_Conflict IsNot Nothing AndAlso Me.IsClosed Then
								DoStopInventory()
								Return
							End If
							Continue Do
						End If
						Throw ex
					End Try
					If item Is Nothing Then ' 为空 表示周围没有标签或者指令结束
						Exit Do
					End If

					If item.Antenna = 0 OrElse item.Antenna > 4 Then
						Continue Do
					End If
					'm_test_flag++;
					SyncLock m_tags ' 加锁，防止被其他线程改变
						Dim sitem As ShowTagItem = Nothing ' 一个标签的结构体
						If m_tags.TryGetValue(item.Code, sitem) Then '判断是否已经盘点出来 根据EPC号码，sitem的值从m_tag中取出
							sitem.IncCount(item) ' 转换成 ShowTagItem 结构体，并保存，这里m_tag2为什么会更新？？
							'Console.WriteLine("sitem：" + sitem.Counts);
							'Console.WriteLine("mtag2：" + m_tags2[0].Counts);
							m_iShowRow = 1
						Else
							sitem = New ShowTagItem(item)
							m_tags.Add(item.Code, sitem) ' 保存到字典
							m_tags2.Add(sitem) ' 保存到列表
							m_iShowRow = 0
						End If
						m_iInvTagCount += 1
						m_iInvTimeMs = Environment.TickCount - m_iInvStartTick + 1
					End SyncLock
					ShowTag()
				Loop
				ShowTag() '将上下位机的时序差导致的未显示的标签显示
				'WriteLog(MessageType.Info, "标签数="+ m_test_flag, null);
				Me.BeginInvoke(New ThreadStart(AddressOf OnInventoryEnd))
			Catch ex As Exception
				Try
					WriteLog(MessageType.Error, "Inventory label failed：", ex)
				Catch
				End Try
				DoStopInventory()
			End Try
		End Sub

		Private Sub btnInventoryActive_Click(ByVal sender As Object, ByVal e As EventArgs) Handles btnConnect.Click
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If

				If cmbWorkmode.SelectedIndex = 0 Then '应答模式,需要下发数据
					If InInventory Then
						StopInventory = True
						CloseInventoryThread()
						reader_Conflict.InventoryStop(s_usStopInventoryTimeout)
						btnInventory.Enabled = True
						Return
					End If
					devicepara.Workmode = CByte(cmbWorkmode.SelectedIndex)
					reader_Conflict.SetDevicePara(devicepara)
					WriteLog(MessageType.Info, "Set parameters successfully:", Nothing)

					m_btInvType = 0
					m_uiInvParam = 0
					btnInventory.Enabled = False
					WriteLog(MessageType.Info, "Start  inventory", Nothing)
					InInventory = True
					StopInventory = False
					System.Threading.Thread.Sleep(100) ' 延时函数 单位ms

					reader_Conflict.Inventory(m_btInvType, m_uiInvParam) ' start inventory
					m_thInventory = New Thread(AddressOf InventoryThread)
					m_thInventory.Start()

				Else '主动模式

					If InInventory Then
						StopInventory = True
						CloseInventoryThread()
						btnInventory.Enabled = True
						Return
					End If
					devicepara.Workmode = CByte(cmbWorkmode.SelectedIndex)
					reader_Conflict.SetDevicePara(devicepara)

					btnInventory.Enabled = False
					InInventory = True
					StopInventory = False

					m_thInventory = New Thread(AddressOf InventoryThread)
					m_thInventory.Start()
				End If

			Catch ex As Exception
				InInventory = False
				StopInventory = True
				btnInventory.Enabled = True

				WriteLog(MessageType.Error, "Inventory label failed：", ex)
				MessageBox.Show(Me, "Inventory label failed：" & ex.Message, "Tips")
			End Try
		End Sub

		Private Sub Form1_FormClosing(ByVal sender As Object, ByVal e As FormClosingEventArgs) Handles MyBase.FormClosing
			m_bClosed = True
			StopInventory = True
		End Sub

		Private Function IsPortsChanged(ByVal ports() As String) As Boolean
			Dim items As ComboBox.ObjectCollection = cmbComPort.Items
			If items.Count <> ports.Length Then
				Return True
			End If

			For i As Integer = 0 To ports.Length - 1
				If String.Compare(items(i).ToString(), ports(i), StringComparison.OrdinalIgnoreCase) <> 0 Then
					Return True
				End If
			Next i
			Return False
		End Function

		Private Sub cmbComPort_DropDown(ByVal sender As Object, ByVal e As EventArgs) Handles cmbComPort.DropDown
			Try
				Dim ports() As String = SerialPort.GetPortNames()
				Array.Sort(ports)
				For i = 0 To ports.Length - 1
					stdSerialData.AppendText(ports(i))
				Next

				If IsPortsChanged(ports) Then
					cmbComPort.Items.Clear()
					cmbComPort.Items.AddRange(ports)
				End If

			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub

		Private Sub btnClear_Click(ByVal sender As Object, ByVal e As EventArgs) Handles btnClear.Click
			lsvTagsActive.Items.Clear()
		End Sub

		Private Sub Form1_Load(ByVal sender As Object, ByVal e As EventArgs) Handles MyBase.Load
			cmbComBaud.SelectedIndex = 4 'default 115200
			'InitReader()
		End Sub

		Private Sub cmbFreqStart_SelectedIndexChanged(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub btnSetFreq_Click(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub btnGetFreq_Click(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub cmbRegion_SelectedValueChanged(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub Close_Realy_Click(ByVal sender As Object, ByVal e As EventArgs)
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If
				WriteLog(MessageType.Info, "Set device Close_Realy", Nothing)
				Dim time As Byte = 0 ' time means Close Second  ,0-alltime
				reader_Conflict.Close_Relay(time)
				WriteLog(MessageType.Info, "Set device Close_Realy Success", Nothing)
			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub

		Private Sub Release_Realy_Click(ByVal sender As Object, ByVal e As EventArgs)
			Try

				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If
				WriteLog(MessageType.Info, "Set device Release_Realy", Nothing)
				Dim time As Byte = 0 ' time means Close Second  ,0-alltime
				reader_Conflict.Release_Relay(time)
				WriteLog(MessageType.Info, "Set device Release_Realy Success", Nothing)
			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub

		Private Sub btnScanUsb_Click(ByVal sender As Object, ByVal e As EventArgs)
			Dim strSN As String = ""
			Dim arrBuffer(255) As Byte
			Dim flag As String = ""
			Dim iHidNumber As Integer = 0
			Dim iIndex As UInt16 = 0
			'cbxusbpath.Items.Clear()
			iHidNumber = m_reader.CFHid_GetUsbCount()
			For iIndex = 0 To iHidNumber - 1
				m_reader.CFHid_GetUsbInfo(iIndex, arrBuffer)
				strSN = System.Text.Encoding.Default.GetString(arrBuffer).Replace(vbNullChar, "")
				flag = strSN.Substring(strSN.Length - 3)
				If flag = "kbd" Then '键盘
					'cbxusbpath.Items.Add("\Keyboard-can'topen")
				Else ' HID
					'cbxusbpath.Items.Add("\USB-open")
				End If
				strSN = "" '需要清零
				arrBuffer = New Byte(255) {} '需要清零
			Next iIndex
			If iHidNumber > 0 Then
				'cbxusbpath.SelectedIndex = 0
			End If
		End Sub

		Private Sub btnUSBopen_Click(ByVal sender As Object, ByVal e As EventArgs)

		End Sub

		Private Sub btnUSBClose_Click(ByVal sender As Object, ByVal e As EventArgs)
			WriteLog(MessageType.Info, "Reader Disconnected", Nothing)

			groupNet.Enabled = True
			'gpbCom.Enabled = True

			'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
			Dim reader_Conflict As Reader = Me.Reader
			If reader_Conflict Is Nothing Then
				Return
			End If

			reader_Conflict.Close()
		End Sub

		Private Sub btnGetOutInterface_Click(ByVal sender As Object, ByVal e As EventArgs)
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If
				WriteLog(MessageType.Info, "Get device interface", Nothing)
				devicepara = reader_Conflict.GetDevicePara()
				Dim interport As Byte
				interport = devicepara.port
				WriteLog(MessageType.Info, "Get device interface Success", Nothing)
			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub

		Private Sub btnSetOutInterface_Click(ByVal sender As Object, ByVal e As EventArgs)
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If
				WriteLog(MessageType.Info, "Set device OutInterface", Nothing)
				devicepara.wieggand = &H0
				' cmbOutInterface.SelectedIndex
				devicepara.port = CByte(2)
				If devicepara.port = 0 Then
					devicepara.port = &H80
				ElseIf devicepara.port = 1 Then
					devicepara.port = &H40
				ElseIf devicepara.port = 2 Then
					devicepara.port = &H20
				ElseIf devicepara.port = 4 Then ' Wifi
					devicepara.port = &H10
				ElseIf devicepara.port = 5 Then ' USB
					devicepara.port = &H1
				ElseIf devicepara.port = 6 Then ' Keyoard
					devicepara.port = &H2
				ElseIf devicepara.port = 7 Then ' CDC_COM
					devicepara.port = &H4
				Else
					devicepara.port = &H80
				End If
				reader_Conflict.SetDevicePara(devicepara)
				WriteLog(MessageType.Info, "Set device OutInterface Success", Nothing)
			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub



		Private Sub Label1_Click(sender As Object, e As EventArgs) Handles Label1.Click

		End Sub

		Private Sub cmbWorkmode_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbWorkmode.SelectedIndexChanged

		End Sub

		Private Sub Label4_Click(sender As Object, e As EventArgs) Handles Label4.Click

		End Sub

		Private Sub label3_Click(sender As Object, e As EventArgs) Handles label3.Click

		End Sub

		Private Sub btnSportClose_Click(sender As Object, e As EventArgs) Handles btnSportClose.Click
			Try
				WriteLog(MessageType.Info, "Close Reader", Nothing)
				cmbComPort.Enabled = True
				btnSportOpen.Enabled = True

				'groupusb.Enabled = True
				groupNet.Enabled = True

				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Return
				End If

				reader_Conflict.Close()
			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub

		Private Sub cmbComBaud_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbComBaud.SelectedIndexChanged

		End Sub

		Private Sub btnDisConnect_Click_1(sender As Object, e As EventArgs) Handles btnDisConnect.Click
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If


				If InInventory Then
					StopInventory = True
					CloseInventoryThread()
					If devicepara.Workmode = 0 Then ' Ans mode
						reader_Conflict.InventoryStop(s_usStopInventoryTimeout)
					End If
					btnInventory.Enabled = True
					Return
				End If
				WriteLog(MessageType.Error, "Inventory Stoped", Nothing)

			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub

		Private Sub btnInventory_Click(sender As Object, e As EventArgs) Handles btnInventory.Click
			Dim time As Byte = 0 ' time means Close Second  ,0-alltime

			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If

				reader_Conflict.Release_Relay(time)

				If cmbWorkmode.SelectedIndex = 0 Then '应答模式,需要下发数据
					If InInventory Then
						StopInventory = True
						CloseInventoryThread()
						reader_Conflict.InventoryStop(s_usStopInventoryTimeout)
						btnInventory.Enabled = True
						Return
					End If
					devicepara.Workmode = CByte(cmbWorkmode.SelectedIndex)
					reader_Conflict.SetDevicePara(devicepara)
					WriteLog(MessageType.Info, "Set parameters successfully:", Nothing)

					m_btInvType = 0
					m_uiInvParam = 0
					btnInventory.Enabled = False
					WriteLog(MessageType.Info, "Start  inventory", Nothing)
					InInventory = True
					StopInventory = False
					System.Threading.Thread.Sleep(100) ' 延时函数 单位ms

					reader_Conflict.Inventory(m_btInvType, m_uiInvParam) ' start inventory
					m_thInventory = New Thread(AddressOf InventoryThread)
					m_thInventory.Start()

				Else '主动模式

					If InInventory Then
						StopInventory = True
						CloseInventoryThread()
						btnInventory.Enabled = True
						Return
					End If
					devicepara.Workmode = CByte(cmbWorkmode.SelectedIndex)
					reader_Conflict.SetDevicePara(devicepara)

					btnInventory.Enabled = False
					InInventory = True
					StopInventory = False

					m_thInventory = New Thread(AddressOf InventoryThread)
					m_thInventory.Start()
				End If

			Catch ex As Exception
				InInventory = False
				StopInventory = True
				btnInventory.Enabled = True

				WriteLog(MessageType.Error, "Inventory label failed：", ex)
				MessageBox.Show(Me, "Inventory label failed：" & ex.Message, "Tips")
			End Try
		End Sub

		Private Sub btnInvStop_Click(sender As Object, e As EventArgs) Handles btninvStop.Click
			Try
				'INSTANT VB NOTE: The variable reader was renamed since Visual Basic does not handle local variables named the same as class members well:
				Dim reader_Conflict As Reader = Me.Reader
				If reader_Conflict Is Nothing Then
					Throw New Exception("Reader not connected")
				End If

				If InInventory Then
					StopInventory = True
					CloseInventoryThread()
					If devicepara.Workmode = 0 Then ' Ans mode
						reader_Conflict.InventoryStop(s_usStopInventoryTimeout)
					End If
					btnInventory.Enabled = True
					Return
				End If
				WriteLog(MessageType.Error, "Inventory Stoped", Nothing)

			Catch ex As Exception
				MessageBox.Show(Me, ex.Message, Me.Text)
			End Try
		End Sub
	End Class
End Namespace
