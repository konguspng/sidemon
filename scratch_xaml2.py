import os

with open("SidebarDiagnostics/Settings.xaml", "r", encoding="utf-8") as f:
    text = f.read()

# Fix Window tags
text = text.replace('SizeToContent="Height"', 'Height="700"')
text = text.replace('ShowMaximize="False"', 'ShowMaximize="False"\n        ResizeMode="CanResize"')

# Fix Monitors Tab
start_monitors = text.find('<TabItem Header="{x:Static frame:Resources.SettingsMonitorsTab}">')
end_monitors = text.find('<TabItem Header="{x:Static frame:Resources.SettingsHotkeysTab}">')

monitors_content = """            <TabItem Header="{x:Static frame:Resources.SettingsMonitorsTab}">
                <DockPanel LastChildFill="True" Margin="0,0,10,10">
                    <StackPanel DockPanel.Dock="Top" Margin="0,0,0,10">
                        <TextBlock Text="Monitors Configuration" Style="{StaticResource SettingsSectionHeader}" />
                        <TextBlock Text="{x:Static frame:Resources.SettingsMonitorsSubtitle1}" Style="{StaticResource SettingsSectionHelper}" />
                        <TextBlock Text="{x:Static frame:Resources.SettingsMonitorsSubtitle2}" Style="{StaticResource SettingsSectionHelper}" Margin="0,0,0,10" />
                    </StackPanel>
                    
                    <DataGrid ItemsSource="{Binding Path=MonitorConfig, Mode=OneWay}" Style="{StaticResource MonitorGrid}" AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False" HeadersVisibility="Column" SelectionMode="Single">
                        <DataGrid.Columns>
                            <DataGridTemplateColumn Header="{x:Static frame:Resources.SettingsMonitorsColumn1}" HeaderStyle="{StaticResource MonitorGridHeaderCenter}" Width="80">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate DataType="{x:Type monitor:MonitorConfig}">
                                        <CheckBox IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" HorizontalAlignment="Center" VerticalAlignment="Center" ToolTip="{x:Static frame:Resources.SettingsMonitorEnabledTooltip}" />
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                            <DataGridTemplateColumn Header="{x:Static frame:Resources.SettingsMonitorsColumn2}" Width="*" HeaderStyle="{StaticResource MonitorGridHeader}">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate DataType="{x:Type monitor:MonitorConfig}">
                                        <Label Content="{Binding Path=Name}" VerticalAlignment="Center" />
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                            <DataGridTemplateColumn Width="70">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate DataType="{x:Type monitor:MonitorConfig}">
                                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                            <Button Content="&#x25B2;" Width="22" Height="22" Margin="0,0,4,0" Click="MonitorUp_Click" ToolTip="Move Up" />
                                            <Button Content="&#x25BC;" Width="22" Height="22" Click="MonitorDown_Click" ToolTip="Move Down" />
                                        </StackPanel>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>
                        <DataGrid.RowDetailsTemplate>
                            <DataTemplate>
                                <Border Style="{StaticResource MonitorDetailsBorder}" Margin="10" Padding="10" Background="#F1F1F1" CornerRadius="4">
                                    <StackPanel Style="{StaticResource VerticalPanel}">
                                        <StackPanel>
                                            <StackPanel.Style>
                                                <Style TargetType="{x:Type StackPanel}">
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Path=Type}" Value="{x:Static monitor:MonitorType.AIUsage}">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </StackPanel.Style>
                                            <TextBlock Text="{x:Static frame:Resources.SettingsUsageNote}" TextWrapping="Wrap" Margin="0,0,0,8" />
                                            
                                            <ListView ItemsSource="{Binding Path=HardwareOC, Mode=OneWay}" ItemContainerStyle="{StaticResource MonitorHardwareItem}" Style="{StaticResource HardwareGrid}">
                                                <ListView.View>
                                                    <GridView AllowsColumnReorder="False">
                                                        <GridViewColumn Header="{x:Static frame:Resources.SettingsHardwareColumn1}" HeaderContainerStyle="{StaticResource MonitorHardwareHeaderCenter}">
                                                            <GridViewColumn.CellTemplate>
                                                                <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                    <CheckBox IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsUsageEnabledTooltip}" Style="{StaticResource MonitorHardwareCheckbox}" />
                                                                </DataTemplate>
                                                            </GridViewColumn.CellTemplate>
                                                        </GridViewColumn>
                                                        <GridViewColumn Header="{x:Static frame:Resources.SettingsUsageProviderColumn}" HeaderContainerStyle="{StaticResource MonitorHardwareHeader}" Width="150">
                                                            <GridViewColumn.CellTemplate>
                                                                <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                    <TextBox Text="{Binding Path=Name, Mode=TwoWay}" Style="{StaticResource MonitorHardwareText}" ToolTip="{x:Static frame:Resources.SettingsHardwareNameTooltip}" />
                                                                </DataTemplate>
                                                            </GridViewColumn.CellTemplate>
                                                        </GridViewColumn>
                                                        <GridViewColumn Header="{x:Static frame:Resources.SettingsUsageCustomPathColumn}" HeaderContainerStyle="{StaticResource MonitorHardwareHeader}" Width="160">
                                                            <GridViewColumn.CellTemplate>
                                                                <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                    <TextBox Text="{Binding Path=CustomPath, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" Style="{StaticResource MonitorHardwareText}" ToolTip="{x:Static frame:Resources.SettingsUsageCustomPathTooltip}" />
                                                                </DataTemplate>
                                                            </GridViewColumn.CellTemplate>
                                                        </GridViewColumn>
                                                        <GridViewColumn Header="{x:Static frame:Resources.SettingsUsageShowExtraColumn}" HeaderContainerStyle="{StaticResource MonitorHardwareHeader}" Width="80">
                                                            <GridViewColumn.CellTemplate>
                                                                <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                    <CheckBox IsChecked="{Binding Path=ShowExtra, TargetNullValue=True, FallbackValue=True, Mode=TwoWay}" HorizontalAlignment="Center" VerticalAlignment="Center">
                                                                        <CheckBox.Style>
                                                                            <Style TargetType="CheckBox" BasedOn="{StaticResource {x:Type CheckBox}}">
                                                                                <Setter Property="Visibility" Value="Hidden" />
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding ID}" Value="AIUsage_Agy">
                                                                                        <Setter Property="Visibility" Value="Visible" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </CheckBox.Style>
                                                                    </CheckBox>
                                                                </DataTemplate>
                                                            </GridViewColumn.CellTemplate>
                                                        </GridViewColumn>
                                                        <GridViewColumn Header="{x:Static frame:Resources.SettingsUsageStatusColumn}" HeaderContainerStyle="{StaticResource MonitorHardwareHeader}" Width="150">
                                                            <GridViewColumn.CellTemplate>
                                                                <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                    <TextBlock Text="{Binding Path=Status, Mode=OneWay}" VerticalAlignment="Center" Margin="5,0,0,0" TextTrimming="CharacterEllipsis" />
                                                                </DataTemplate>
                                                            </GridViewColumn.CellTemplate>
                                                        </GridViewColumn>
                                                        <GridViewColumn Width="70">
                                                            <GridViewColumn.CellTemplate>
                                                                <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                                                        <Button Content="&#x25B2;" Width="22" Height="22" Margin="0,0,4,0" Click="HardwareUp_Click" ToolTip="Move Up" />
                                                                        <Button Content="&#x25BC;" Width="22" Height="22" Click="HardwareDown_Click" ToolTip="Move Down" />
                                                                    </StackPanel>
                                                                </DataTemplate>
                                                            </GridViewColumn.CellTemplate>
                                                        </GridViewColumn>
                                                    </GridView>
                                                </ListView.View>
                                            </ListView>
                                        </StackPanel>

                                        <ListView ItemsSource="{Binding Path=HardwareOC, Mode=OneWay}" ItemContainerStyle="{StaticResource MonitorHardwareItem}">
                                            <ListView.Style>
                                                <Style TargetType="{x:Type ListView}" BasedOn="{StaticResource HardwareGrid}">
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Path=Type}" Value="{x:Static monitor:MonitorType.AIUsage}">
                                                            <Setter Property="Visibility" Value="Collapsed" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </ListView.Style>
                                            <ListView.View>
                                                <GridView AllowsColumnReorder="False">
                                                    <GridViewColumn Header="{x:Static frame:Resources.SettingsHardwareColumn1}" HeaderContainerStyle="{StaticResource MonitorHardwareHeaderCenter}">
                                                        <GridViewColumn.CellTemplate>
                                                            <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                <CheckBox IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsHardwareEnabledTooltip}" Style="{StaticResource MonitorHardwareCheckbox}" />
                                                            </DataTemplate>
                                                        </GridViewColumn.CellTemplate>
                                                    </GridViewColumn>
                                                    <GridViewColumn Header="{x:Static frame:Resources.SettingsHardwareColumn2}" HeaderContainerStyle="{StaticResource MonitorHardwareHeader}" Width="190">
                                                        <GridViewColumn.CellTemplate>
                                                            <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                <TextBox Text="{Binding Path=Name, Mode=TwoWay}" Style="{StaticResource MonitorHardwareText}" ToolTip="{x:Static frame:Resources.SettingsHardwareNameTooltip}" />
                                                            </DataTemplate>
                                                        </GridViewColumn.CellTemplate>
                                                    </GridViewColumn>
                                                    <GridViewColumn Width="70">
                                                        <GridViewColumn.CellTemplate>
                                                            <DataTemplate DataType="{x:Type monitor:HardwareConfig}">
                                                                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                                                    <Button Content="&#x25B2;" Width="22" Height="22" Margin="0,0,4,0" Click="HardwareUp_Click" ToolTip="Move Up" />
                                                                    <Button Content="&#x25BC;" Width="22" Height="22" Click="HardwareDown_Click" ToolTip="Move Down" />
                                                                </StackPanel>
                                                            </DataTemplate>
                                                        </GridViewColumn.CellTemplate>
                                                    </GridViewColumn>
                                                </GridView>
                                            </ListView.View>
                                        </ListView>

                                        <DockPanel LastChildFill="True" Margin="0,15,0,0">
                                            <Label DockPanel.Dock="Left" Content="{x:Static frame:Resources.SettingsMetrics}" Margin="0,0,10,0" VerticalAlignment="Top" />
                                            <ItemsControl ItemsSource="{Binding Path=Metrics, Mode=OneWay}" ToolTip="{x:Static frame:Resources.SettingsMetricsTooltip}">
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate DataType="{x:Type monitor:MetricConfig}">
                                                        <CheckBox Content="{Binding Path=Name, Mode=OneWay}" IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Margin="0,2,10,2" />
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                                <ItemsControl.ItemsPanel>
                                                    <ItemsPanelTemplate>
                                                        <WrapPanel Orientation="Horizontal" />
                                                    </ItemsPanelTemplate>
                                                </ItemsControl.ItemsPanel>
                                            </ItemsControl>
                                        </DockPanel>

                                        <ItemsControl ItemsSource="{Binding Path=Params}" Margin="10">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate DataType="{x:Type monitor:ConfigParam}">
                                                    <ContentControl>
                                                        <ContentControl.Style>
                                                            <Style TargetType="{x:Type ContentControl}">
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding TypeString}" Value="System.Boolean">
                                                                        <Setter Property="Content">
                                                                            <Setter.Value>
                                                                                <CheckBox Content="{Binding Path=Name}" IsChecked="{Binding Path=Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{Binding Path=Tooltip, Mode=OneTime}" />
                                                                            </Setter.Value>
                                                                        </Setter>
                                                                    </DataTrigger>
                                                                    <DataTrigger Binding="{Binding TypeString}" Value="System.Int32">
                                                                        <Setter Property="Content">
                                                                            <Setter.Value>
                                                                                <StackPanel>
                                                                                    <Label Content="{Binding Path=Name, Mode=OneTime}" />
                                                                                    <TextBox Text="{Binding Path=Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, Converter={StaticResource IntConverter}}" ToolTip="{Binding Path=Tooltip, Mode=OneTime}" />
                                                                                </StackPanel>
                                                                            </Setter.Value>
                                                                        </Setter>
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </ContentControl.Style>
                                                        <ContentControl.Resources>
                                                            <Style TargetType="{x:Type CheckBox}">
                                                                <Setter Property="VerticalAlignment" Value="Center" />
                                                                <Setter Property="Margin" Value="0,5" />
                                                            </Style>
                                                            
                                                            <Style TargetType="{x:Type StackPanel}">
                                                                <Setter Property="Orientation" Value="Horizontal" />
                                                                <Setter Property="Margin" Value="0,5" />
                                                                <Style.Resources>
                                                                    <Style TargetType="{x:Type Label}">
                                                                        <Setter Property="VerticalAlignment" Value="Center" />
                                                                        <Setter Property="Margin" Value="0,0,10,0" />
                                                                        <Setter Property="Padding" Value="0" />
                                                                    </Style>
                                                                    <Style TargetType="{x:Type TextBox}">
                                                                        <Setter Property="VerticalAlignment" Value="Center" />
                                                                        <Setter Property="Width" Value="40" />
                                                                        <Setter Property="MaxLines" Value="1" />
                                                                    </Style>
                                                                </Style.Resources>
                                                            </Style>
                                                        </ContentControl.Resources>
                                                    </ContentControl>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                            <ItemsControl.ItemsPanel>
                                                <ItemsPanelTemplate>
                                                    <StackPanel Style="{StaticResource VerticalPanel}" />
                                                </ItemsPanelTemplate>
                                            </ItemsControl.ItemsPanel>
                                        </ItemsControl>
                                    </StackPanel>
                                </Border>
                            </DataTemplate>
                        </DataGrid.RowDetailsTemplate>
                    </DataGrid>
                </DockPanel>
            </TabItem>
"""
text = text[:start_monitors] + monitors_content + text[end_monitors:]

# Fix Hotkeys Tab
start_hotkeys = text.find('<TabItem Header="{x:Static frame:Resources.SettingsHotkeysTab}">')
end_hotkeys = text.find('<TabItem Header="{x:Static frame:Resources.SettingsOverlayTab}">')

hotkeys_content = """            <TabItem Header="{x:Static frame:Resources.SettingsHotkeysTab}">
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <StackPanel Margin="0,0,10,10">
                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="Keyboard Shortcuts" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="Click the record button then press a key combination to bind an action." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="100" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                    </Grid.RowDefinitions>

                                    <Label Grid.Column="0" Grid.Row="0" Content="{x:Static frame:Resources.SettingsHotkeyToggle}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="0" Content="Record" Click="BindToggle_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="0" Text="{Binding Path=ToggleKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />

                                    <Label Grid.Column="0" Grid.Row="1" Content="{x:Static frame:Resources.SettingsHotkeyShow}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="1" Content="Record" Click="BindShow_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="1" Text="{Binding Path=ShowKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />

                                    <Label Grid.Column="0" Grid.Row="2" Content="{x:Static frame:Resources.SettingsHotkeyHide}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="2" Content="Record" Click="BindHide_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="2" Text="{Binding Path=HideKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />

                                    <Label Grid.Column="0" Grid.Row="3" Content="{x:Static frame:Resources.SettingsHotkeyCycleEdge}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="3" Content="Record" Click="BindCycleEdge_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="3" Text="{Binding Path=CycleEdgeKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />

                                    <Label Grid.Column="0" Grid.Row="4" Content="{x:Static frame:Resources.SettingsHotkeyCycleScreen}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="4" Content="Record" Click="BindCycleScreen_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="4" Text="{Binding Path=CycleScreenKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />

                                    <Label Grid.Column="0" Grid.Row="5" Content="{x:Static frame:Resources.SettingsHotkeyReserveSpace}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="5" Content="Record" Click="BindReserveSpace_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="5" Text="{Binding Path=ReserveSpaceKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />

                                    <Label Grid.Column="0" Grid.Row="6" Content="{x:Static frame:Resources.SettingsHotkeyReload}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="6" Content="Record" Click="BindReload_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="6" Text="{Binding Path=ReloadKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />

                                    <Label Grid.Column="0" Grid.Row="7" Content="{x:Static frame:Resources.SettingsHotkeyClose}" />
                                    <ToggleButton Grid.Column="1" Grid.Row="7" Content="Record" Click="BindClose_Click" LostFocus="BindButton_LostFocus" Style="{StaticResource HotkeyToggle}" />
                                    <TextBox Grid.Column="2" Grid.Row="7" Text="{Binding Path=CloseKey, Mode=OneWay, Converter={StaticResource HotkeyConverter}}" Style="{StaticResource HotkeyLabel}" IsReadOnly="True" />
                                </Grid>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
"""
text = text[:start_hotkeys] + hotkeys_content + text[end_hotkeys:]

# Fix Overlay Tab
start_overlay = text.find('<TabItem Header="{x:Static frame:Resources.SettingsOverlayTab}">')
end_overlay = text.find('</TabControl>')

overlay_content = """            <TabItem Header="{x:Static frame:Resources.SettingsOverlayTab}">
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <StackPanel Margin="0,0,10,10">
                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="FPS Overlay" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="Shows a small, click-through FPS counter at a screen corner by reading RivaTuner Statistics Server's (RTSS) live frame stats. Requires RTSS to be installed and running." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>

                                    <Label Grid.Column="0" Grid.Row="0" Content="Show FPS Overlay" />
                                    <CheckBox Grid.Column="1" Grid.Row="0" IsChecked="{Binding Path=ShowFpsOverlay, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Toggle FPS overlay." />

                                    <Label Grid.Column="0" Grid.Row="1" Content="Overlay Corner" IsEnabled="{Binding Path=ShowFpsOverlay, Mode=OneWay}" />
                                    <ComboBox Grid.Column="1" Grid.Row="1" Width="200" HorizontalAlignment="Left" IsEnabled="{Binding Path=ShowFpsOverlay, Mode=OneWay}" ItemsSource="{Binding Path=OverlayCornerItems, Mode=OneWay}" DisplayMemberPath="Text" SelectedValuePath="Value" SelectedValue="{Binding Path=FpsOverlayCorner, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Which screen corner the FPS overlay appears in." />

                                    <Label Grid.Column="0" Grid.Row="2" Content="Overlay Opacity" IsEnabled="{Binding Path=ShowFpsOverlay, Mode=OneWay}" />
                                    <DockPanel Grid.Column="1" Grid.Row="2" HorizontalAlignment="Left" Width="250" IsEnabled="{Binding Path=ShowFpsOverlay, Mode=OneWay}">
                                        <TextBlock DockPanel.Dock="Right" Text="%" VerticalAlignment="Center" Margin="5,0,0,0" Foreground="#888" />
                                        <TextBox DockPanel.Dock="Right" Text="{Binding Path=FpsOverlayOpacity, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:F2}}" Width="40" Margin="5,0,0,0" PreviewTextInput="NumberBox_PreviewTextInput" />
                                        <Slider Value="{Binding Path=FpsOverlayOpacity, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="0.2" Maximum="1.0" ToolTip="Overlay transparency." />
                                    </DockPanel>

                                    <TextBlock Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="3" Margin="0,16,0,0" Style="{StaticResource SettingsHint}" TextWrapping="Wrap" Text="RTSS not detected. Install and run RivaTuner Statistics Server for the overlay to show live FPS; without it, the overlay shows &quot;No RTSS&quot;." Visibility="{Binding Path=RTSSDetected, Converter={StaticResource BoolToVisibility}, ConverterParameter=Invert}" />
                                    <TextBlock Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="3" Margin="0,16,0,0" Style="{StaticResource SettingsHint}" Text="RTSS detected. The overlay will show live FPS for the app currently in focus." Visibility="{Binding Path=RTSSDetected, Converter={StaticResource BoolToVisibility}}" />
                                </Grid>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
        """
text = text[:start_overlay] + overlay_content + text[end_overlay:]

with open("SidebarDiagnostics/Settings.xaml", "w", encoding="utf-8") as f:
    f.write(text)

print("Finished rewriting Settings.xaml")
