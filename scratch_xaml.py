import os

xaml_content = """        <TabControl Style="{StaticResource SettingTab}" Grid.IsSharedSizeScope="True">
            <TabItem Header="{x:Static frame:Resources.SettingsGeneralTab}">
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <StackPanel Margin="0,0,10,10">
                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="Basic Settings" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="General application behavior." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                    </Grid.RowDefinitions>
                                    <Label Grid.Column="0" Grid.Row="0" Content="{x:Static frame:Resources.SettingsLanguage}" />
                                    <ComboBox Grid.Column="1" Grid.Row="0" Width="200" HorizontalAlignment="Left" ItemsSource="{Binding Path=CultureItems, Mode=OneWay}" DisplayMemberPath="Text" SelectedValuePath="Value" SelectedValue="{Binding Path=Culture, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsLanguageTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="1" Content="{x:Static frame:Resources.SettingsRunAtStartup}" />
                                    <CheckBox Grid.Column="1" Grid.Row="1" IsChecked="{Binding Path=RunAtStartup, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsRunAtStartupTooltip}" />

                                    <Label Grid.Column="0" Grid.Row="2" Content="{x:Static frame:Resources.SettingsShowTrayIcon}" />
                                    <CheckBox Grid.Column="1" Grid.Row="2" x:Name="ShowTrayIconCheckbox" Unchecked="ShowTrayIconCheckbox_Unchecked" IsChecked="{Binding Path=ShowTrayIcon, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsShowTrayIconTooltip}" />

                                    <Label Grid.Column="0" Grid.Row="3" Content="{x:Static frame:Resources.SettingsPollingInterval}" />
                                    <DockPanel Grid.Column="1" Grid.Row="3" HorizontalAlignment="Left" Width="250">
                                        <TextBlock DockPanel.Dock="Right" Text="ms" VerticalAlignment="Center" Margin="5,0,0,0" Foreground="#888" />
                                        <TextBox DockPanel.Dock="Right" Text="{Binding ElementName=PollingIntervalSlider, Path=Value, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:0}}" Width="40" Margin="5,0,0,0" PreviewTextInput="NumberBox_PreviewTextInput" />
                                        <Slider x:Name="PollingIntervalSlider" Value="{Binding Path=PollingInterval, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="100" Maximum="5000" TickFrequency="100" LargeChange="1000" ToolTip="{x:Static frame:Resources.SettingsPollingIntervalTooltip}" />
                                    </DockPanel>
                                </Grid>
                            </StackPanel>
                        </Border>
                        
                        <Expander Header="Advanced Driver Options" Margin="0,0,0,16">
                            <Border Style="{StaticResource SettingsSectionBorder}" Margin="0">
                                <StackPanel>
                                    <StackPanel Orientation="Vertical" Visibility="{Binding Path=PawnIODriverInstalled, Converter={StaticResource BoolToVisibility}, ConverterParameter=Invert}">
                                        <TextBlock TextWrapping="Wrap" Opacity="0.75" FontSize="12" Margin="0,0,0,8" Text="CPU and GPU sensor readings (clock, temperature, voltage) need the PawnIO driver, a small signed driver from pawnio.eu. Without it those readings show &quot;No Value&quot;." />
                                        <StackPanel Orientation="Horizontal">
                                            <Button Content="Install PawnIO Driver" HorizontalAlignment="Left" Click="InstallPawnIO_Click" IsEnabled="{Binding Path=PawnIODriverInstalling, Converter={StaticResource BoolInverseConverter}}" />
                                            <ProgressBar IsIndeterminate="True" Width="120" Height="6" Margin="10,0,0,0" VerticalAlignment="Center" Visibility="{Binding Path=PawnIODriverInstalling, Converter={StaticResource BoolToVisibility}}" />
                                        </StackPanel>
                                    </StackPanel>
                                    <TextBlock Margin="0,8,0,0" Opacity="0.75" FontStyle="Italic" Text="PawnIO driver: installed. CPU/GPU sensors are available." Visibility="{Binding Path=PawnIODriverInstalled, Converter={StaticResource BoolToVisibility}}" />
                                </StackPanel>
                            </Border>
                        </Expander>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
            
            <TabItem Header="{x:Static frame:Resources.SettingsAdvancedTab}">
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <StackPanel Margin="0,0,10,10">
                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="Position &amp; Size" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="Configure where and how the sidebar appears on your monitors." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>
                                    
                                    <Label Grid.Column="0" Grid.Row="0" Content="{x:Static frame:Resources.SettingsDock}" />
                                    <ComboBox Grid.Column="1" Grid.Row="0" Width="200" HorizontalAlignment="Left" ItemsSource="{Binding Path=DockEdgeItems, Mode=OneWay}" DisplayMemberPath="Text" SelectedValuePath="Value" SelectedValue="{Binding Path=DockEdge, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsDockTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="1" Content="{x:Static frame:Resources.SettingsScreen}" />
                                    <ComboBox Grid.Column="1" Grid.Row="1" Width="200" HorizontalAlignment="Left" ItemsSource="{Binding Path=ScreenItems, Mode=OneWay}" DisplayMemberPath="Text" SelectedValuePath="Index" SelectedValue="{Binding Path=ScreenIndex, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsScreenTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="2" Content="{x:Static frame:Resources.SettingsHorizontalOffset}" />
                                    <DockPanel Grid.Column="1" Grid.Row="2" HorizontalAlignment="Left" Width="250">
                                        <TextBlock DockPanel.Dock="Right" Text="px" VerticalAlignment="Center" Margin="5,0,0,0" Foreground="#888" />
                                        <TextBox DockPanel.Dock="Right" Text="{Binding ElementName=XOffsetSlider, Path=Value, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:0}}" Width="40" Margin="5,0,0,0" PreviewTextInput="NumberBox_PreviewTextInput" />
                                        <Slider x:Name="XOffsetSlider" Value="{Binding Path=XOffset, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="-2000" Maximum="2000" TickFrequency="1" LargeChange="1000" ValueChanged="OffsetSlider_ValueChanged" ToolTip="{x:Static frame:Resources.SettingsHorizontalOffsetTooltip}" />
                                    </DockPanel>

                                    <Label Grid.Column="0" Grid.Row="3" Content="{x:Static frame:Resources.SettingsVerticalOffset}" />
                                    <DockPanel Grid.Column="1" Grid.Row="3" HorizontalAlignment="Left" Width="250">
                                        <TextBlock DockPanel.Dock="Right" Text="px" VerticalAlignment="Center" Margin="5,0,0,0" Foreground="#888" />
                                        <TextBox DockPanel.Dock="Right" Text="{Binding ElementName=YOffsetSlider, Path=Value, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:0}}" Width="40" Margin="5,0,0,0" PreviewTextInput="NumberBox_PreviewTextInput" />
                                        <Slider x:Name="YOffsetSlider" Value="{Binding Path=YOffset, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="-2000" Maximum="2000" TickFrequency="1" LargeChange="1000" ValueChanged="OffsetSlider_ValueChanged" ToolTip="{x:Static frame:Resources.SettingsVerticalOffsetTooltip}" />
                                    </DockPanel>

                                    <Label Grid.Column="0" Grid.Row="4" Content="{x:Static frame:Resources.SettingsSidebarWidth}" />
                                    <DockPanel Grid.Column="1" Grid.Row="4" HorizontalAlignment="Left" Width="250">
                                        <TextBlock DockPanel.Dock="Right" Text="px" VerticalAlignment="Center" Margin="5,0,0,0" Foreground="#888" />
                                        <TextBox DockPanel.Dock="Right" Text="{Binding ElementName=SidebarWidthSlider, Path=Value, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:0}}" Width="40" Margin="5,0,0,0" PreviewTextInput="NumberBox_PreviewTextInput" />
                                        <Slider x:Name="SidebarWidthSlider" Value="{Binding Path=SidebarWidth, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="100" Maximum="300" TickFrequency="5" LargeChange="100" ToolTip="{x:Static frame:Resources.SettingsSidebarWidthTooltip}" />
                                    </DockPanel>
                                    
                                    <Label Grid.Column="0" Grid.Row="5" Content="{x:Static frame:Resources.SettingsUIScale}" />
                                    <DockPanel Grid.Column="1" Grid.Row="5" HorizontalAlignment="Left" Width="250">
                                        <TextBlock DockPanel.Dock="Right" Text="x" VerticalAlignment="Center" Margin="5,0,0,0" Foreground="#888" />
                                        <TextBox DockPanel.Dock="Right" Text="{Binding ElementName=UIScaleSlider, Path=Value, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:F2}}" Width="40" Margin="5,0,0,0" PreviewTextInput="NumberBox_PreviewTextInput" />
                                        <Slider x:Name="UIScaleSlider" Value="{Binding Path=UIScale, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="0.5" Maximum="3.0" TickFrequency="0.05" IsSnapToTickEnabled="True" LargeChange="0.5" ToolTip="{x:Static frame:Resources.SettingsUIScaleTooltip}" />
                                    </DockPanel>
                                    
                                    <StackPanel Grid.Column="1" Grid.Row="6" Margin="0,2,0,4">
                                        <CheckBox Content="{x:Static frame:Resources.SettingsAutoFitScale}" IsChecked="{Binding Path=AutoFitScale}" Margin="0,0,0,4" ToolTip="{x:Static frame:Resources.SettingsAutoFitScaleTooltip}" />
                                        <TextBlock TextWrapping="Wrap" FontSize="11" Margin="25,0,0,0" Foreground="#888">
                                            <TextBlock.Style>
                                                <Style TargetType="{x:Type TextBlock}">
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                    <Style.Triggers>
                                                        <MultiDataTrigger>
                                                            <MultiDataTrigger.Conditions>
                                                                <Condition Binding="{Binding Path=AutoFitScale, Mode=OneWay}" Value="True" />
                                                                <Condition Binding="{Binding Path=UIScaleCapped, Mode=OneWay}" Value="True" />
                                                            </MultiDataTrigger.Conditions>
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </MultiDataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                            <TextBlock.Text>
                                                <Binding Path="MaxUIScale" Mode="OneWay" StringFormat="Auto-fit reduced the scale to {0:F2} because your monitors/drives don't fit the screen height." />
                                            </TextBlock.Text>
                                        </TextBlock>
                                    </StackPanel>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="Window Behavior" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="How the sidebar interacts with other windows." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                    </Grid.RowDefinitions>
                                    
                                    <Label Grid.Column="0" Grid.Row="0" Content="{x:Static frame:Resources.SettingsReserveSpace}" />
                                    <CheckBox Grid.Column="1" Grid.Row="0" IsChecked="{Binding Path=UseAppBar, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsReserveSpaceTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="1" Content="{x:Static frame:Resources.SettingsAlwaysOnTop}" IsEnabled="{Binding Path=BGModeBlur, Mode=OneWay, Converter={StaticResource BoolInverseConverter}}" />
                                    <StackPanel Grid.Column="1" Grid.Row="1" Orientation="Vertical" VerticalAlignment="Center">
                                        <CheckBox IsChecked="{Binding Path=AlwaysTop, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" IsEnabled="{Binding Path=BGModeBlur, Mode=OneWay, Converter={StaticResource BoolInverseConverter}}" ToolTip="Keep the sidebar above other windows. Not available with the Desktop Blur background." />
                                        <TextBlock Text="Not available with Desktop Blur." Style="{StaticResource SettingsHint}" Margin="25,2,0,0" Visibility="{Binding Path=BGModeBlur, Mode=OneWay, Converter={StaticResource BoolToVisibility}}" />
                                    </StackPanel>

                                    <Label Grid.Column="0" Grid.Row="2" Content="{x:Static frame:Resources.SettingsClickThrough}" />
                                    <CheckBox Grid.Column="1" Grid.Row="2" x:Name="ClickThroughCheckbox" Checked="ClickThroughCheckbox_Checked" IsChecked="{Binding Path=ClickThrough, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsClickThroughTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="3" Content="{x:Static frame:Resources.SettingsInitiallyHidden}" />
                                    <CheckBox Grid.Column="1" Grid.Row="3" IsChecked="{Binding Path=InitiallyHidden, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsInitiallyHiddenTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="4" Content="{x:Static frame:Resources.SettingsCollapseMenuBar}" />
                                    <CheckBox Grid.Column="1" Grid.Row="4" IsChecked="{Binding Path=CollapseMenuBar, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsCollapseMenuBarTooltip}" />
                                </Grid>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>

            <TabItem Header="{x:Static frame:Resources.SettingsCustomizeTab}">
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <StackPanel Margin="0,0,10,10">
                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="Background &amp; Panels" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="Configure the visual style of the sidebar and its monitoring cards." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                    </Grid.RowDefinitions>
                                    
                                    <Label Grid.Column="0" Grid.Row="0" Content="Style" VerticalAlignment="Top" Margin="0,5,15,0" />
                                    <StackPanel Grid.Column="1" Grid.Row="0" Margin="0,5,0,5">
                                        <RadioButton GroupName="BGMode" Content="Solid Color" IsChecked="{Binding Path=BGModeSolid, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="One flat background color of your choice." Margin="0,0,0,4" />
                                        <RadioButton GroupName="BGMode" Content="Windows Accent Color" IsChecked="{Binding Path=BGModeAccent, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Follows the accent color from your Windows personalization settings." Margin="0,0,0,4" />
                                        <RadioButton GroupName="BGMode" Content="Desktop Blur (Acrylic)" IsChecked="{Binding Path=BGModeBlur, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Real Windows acrylic, the same live blur as the taskbar and Start menu." />
                                        <TextBlock Text="Real Windows acrylic. Blends into wallpaper. Requires Windows 10 (1803+)." Style="{StaticResource SettingsHint}" Margin="20,2,0,4" />
                                    </StackPanel>
                                    
                                    <Label Grid.Column="0" Grid.Row="1" Content="Color / Tint" IsEnabled="{Binding Path=BGModeAccent, Mode=OneWay, Converter={StaticResource BoolInverseConverter}}" />
                                    <DockPanel Grid.Column="1" Grid.Row="1" IsEnabled="{Binding Path=BGModeAccent, Mode=OneWay, Converter={StaticResource BoolInverseConverter}}" HorizontalAlignment="Left" Width="200" LastChildFill="True">
                                        <Border DockPanel.Dock="Right" Width="22" Height="22" Margin="5,0,0,0" BorderBrush="#888" BorderThickness="1" Background="{Binding Path=BGColor, Mode=OneWay}" Tag="BGColor" MouseLeftButtonDown="ColorBorder_MouseLeftButtonDown" Cursor="Hand" ToolTip="Click to select the color." />
                                        <TextBox Width="Auto" Text="{Binding Path=BGColor, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" ToolTip="The background color. In Desktop Blur mode this is the tint." />
                                    </DockPanel>

                                    <Label Grid.Column="0" Grid.Row="2" Content="Opacity" />
                                    <DockPanel Grid.Column="1" Grid.Row="2" HorizontalAlignment="Left" Width="250">
                                        <TextBlock DockPanel.Dock="Right" Text="%" VerticalAlignment="Center" Margin="5,0,0,0" Foreground="#888" />
                                        <TextBox DockPanel.Dock="Right" Text="{Binding ElementName=BGOpacitySlider, Path=Value, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:F2}}" Width="40" Margin="5,0,0,0" PreviewTextInput="NumberBox_PreviewTextInput" />
                                        <Slider x:Name="BGOpacitySlider" Value="{Binding Path=BGOpacity, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="0.01" Maximum="1.0" LargeChange="0.1" TickFrequency="0.01" ToolTip="How solid the background is." />
                                    </DockPanel>
                                    
                                    <Label Grid.Column="0" Grid.Row="3" Content="Card Panels" />
                                    <CheckBox Grid.Column="1" Grid.Row="3" IsChecked="{Binding Path=UseCardStyle, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Show each monitor group as a rounded card." />
                                    
                                    <Label Grid.Column="0" Grid.Row="4" Content="Colored Bars" />
                                    <CheckBox Grid.Column="1" Grid.Row="4" IsChecked="{Binding Path=ShowAccentBars, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Show a slim colored load bar under percentage readings." />
                                    
                                    <Label Grid.Column="0" Grid.Row="5" Content="Accent Color" IsEnabled="{Binding Path=AccentColorEnabled}" />
                                    <DockPanel Grid.Column="1" Grid.Row="5" IsEnabled="{Binding Path=AccentColorEnabled}" HorizontalAlignment="Left" Width="200" LastChildFill="True">
                                        <Border DockPanel.Dock="Right" Width="22" Height="22" Margin="5,0,0,0" BorderBrush="#888" BorderThickness="1" Background="{Binding Path=AccentColor, Mode=OneWay}" Tag="AccentColor" MouseLeftButtonDown="ColorBorder_MouseLeftButtonDown" Cursor="Hand" ToolTip="Click to select accent color." />
                                        <TextBox Width="Auto" Text="{Binding Path=AccentColor, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" ToolTip="Accent color used for load bars." />
                                    </DockPanel>
                                </Grid>
                            </StackPanel>
                        </Border>
                        
                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="Text &amp; Alignment" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="Font settings and alignment." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                    </Grid.RowDefinitions>
                                    
                                    <Label Grid.Column="0" Grid.Row="0" Content="Vertical Align" />
                                    <ComboBox Grid.Column="1" Grid.Row="0" Width="200" HorizontalAlignment="Left" ItemsSource="{Binding Path=ContentVerticalAlignItems, Mode=OneWay}" DisplayMemberPath="Text" SelectedValuePath="Value" SelectedValue="{Binding Path=ContentVerticalAlign, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Where the sidebar's content sits vertically when shorter than screen." />
                                    
                                    <Label Grid.Column="0" Grid.Row="1" Content="{x:Static frame:Resources.SettingsTextAlign}" />
                                    <ComboBox Grid.Column="1" Grid.Row="1" Width="200" HorizontalAlignment="Left" ItemsSource="{Binding Path=TextAlignItems, Mode=OneWay}" DisplayMemberPath="Text" SelectedValuePath="Value" SelectedValue="{Binding Path=TextAlign, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsTextAlignTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="2" Content="Font" />
                                    <ComboBox Grid.Column="1" Grid.Row="2" Width="200" HorizontalAlignment="Left" ItemsSource="{Binding Path=FontFamilyItems, Mode=OneWay}" SelectedValue="{Binding Path=FontOption, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="Font used for all sidebar text.">
                                        <ComboBox.ItemTemplate>
                                            <DataTemplate>
                                                <TextBlock Text="{Binding Path=Name, Mode=OneWay}" FontFamily="{Binding Path=Family, Mode=OneWay}" FontSize="14" />
                                            </DataTemplate>
                                        </ComboBox.ItemTemplate>
                                    </ComboBox>
                                    
                                    <Label Grid.Column="0" Grid.Row="3" Content="{x:Static frame:Resources.SettingsFontSize}" />
                                    <ComboBox Grid.Column="1" Grid.Row="3" Width="80" HorizontalAlignment="Left" ItemsSource="{Binding Path=FontSettingItems, Mode=OneWay}" DisplayMemberPath="FontSize" SelectedValue="{Binding Path=FontSetting, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsFontSizeTooltip}" />

                                    <Label Grid.Column="0" Grid.Row="4" Content="{x:Static frame:Resources.SettingsFontColor}" />
                                    <DockPanel Grid.Column="1" Grid.Row="4" HorizontalAlignment="Left" Width="200" LastChildFill="True">
                                        <Border DockPanel.Dock="Right" Width="22" Height="22" Margin="5,0,0,0" BorderBrush="#888" BorderThickness="1" Background="{Binding Path=FontColor, Mode=OneWay}" Tag="FontColor" MouseLeftButtonDown="ColorBorder_MouseLeftButtonDown" Cursor="Hand" ToolTip="Click to select font color." />
                                        <TextBox Width="Auto" Text="{Binding Path=FontColor, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" ToolTip="{x:Static frame:Resources.SettingsFontColorTooltip}" />
                                    </DockPanel>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <Border Style="{StaticResource SettingsSectionBorder}">
                            <StackPanel>
                                <TextBlock Text="Header &amp; Alerts" Style="{StaticResource SettingsSectionHeader}" />
                                <TextBlock Text="System information header and high-load alerts." Style="{StaticResource SettingsSectionHelper}" />
                                <Grid Style="{StaticResource SettingGrid}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition SharedSizeGroup="LabelCol" Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                        <RowDefinition Height="38px"/>
                                    </Grid.RowDefinitions>
                                    
                                    <Label Grid.Column="0" Grid.Row="0" Content="{x:Static frame:Resources.SettingsShowMachineName}" />
                                    <CheckBox Grid.Column="1" Grid.Row="0" IsChecked="{Binding Path=ShowMachineName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsShowMachineNameTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="1" Content="{x:Static frame:Resources.SettingsShowClock}" />
                                    <CheckBox Grid.Column="1" Grid.Row="1" IsChecked="{Binding Path=ShowClock, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsShowClockTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="2" Content="{x:Static frame:Resources.Settings24HourClock}" IsEnabled="{Binding Path=ShowClock, Mode=OneWay}" />
                                    <CheckBox Grid.Column="1" Grid.Row="2" IsChecked="{Binding Path=Clock24HR, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" IsEnabled="{Binding Path=ShowClock, Mode=OneWay}" ToolTip="{x:Static frame:Resources.Settings24HourClockTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="3" Content="{x:Static frame:Resources.SettingsDateFormat}" IsEnabled="{Binding Path=ShowClock, Mode=OneWay}" />
                                    <ComboBox Grid.Column="1" Grid.Row="3" Width="200" HorizontalAlignment="Left" ItemsSource="{Binding Path=DateSettingItems}" DisplayMemberPath="Display" SelectedValue="{Binding Path=DateSetting, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" IsEnabled="{Binding Path=ShowClock, Mode=OneWay}" ToolTip="{x:Static frame:Resources.SettingsDateFormatTooltip}" />
                                    
                                    <Label Grid.Column="0" Grid.Row="4" Content="{x:Static frame:Resources.SettingsAlertFontColor}" />
                                    <DockPanel Grid.Column="1" Grid.Row="4" HorizontalAlignment="Left" Width="200" LastChildFill="True">
                                        <Border DockPanel.Dock="Right" Width="22" Height="22" Margin="5,0,0,0" BorderBrush="#888" BorderThickness="1" Background="{Binding Path=AlertFontColor, Mode=OneWay}" Tag="AlertFontColor" MouseLeftButtonDown="ColorBorder_MouseLeftButtonDown" Cursor="Hand" ToolTip="Click to select alert color." />
                                        <TextBox Width="Auto" Text="{Binding Path=AlertFontColor, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" ToolTip="{x:Static frame:Resources.SettingsAlertFontColorTooltip}" />
                                    </DockPanel>

                                    <Label Grid.Column="0" Grid.Row="5" Content="{x:Static frame:Resources.SettingsAlertBlink}" />
                                    <CheckBox Grid.Column="1" Grid.Row="5" IsChecked="{Binding Path=AlertBlink, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsAlertBlinkTooltip}" />
                                </Grid>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
"""
import re
with open("SidebarDiagnostics/Settings.xaml", "r", encoding="utf-8") as f:
    text = f.read()

# Replace TabControl with my string, up to SettingsMonitorsTab
start_idx = text.find('<TabControl Style="{StaticResource SettingTab}">')
if start_idx == -1:
    start_idx = text.find('<TabControl Style="{StaticResource SettingTab}" Grid.IsSharedSizeScope="True">')

end_idx = text.find('<TabItem Header="{x:Static frame:Resources.SettingsMonitorsTab}">')

if start_idx != -1 and end_idx != -1:
    new_text = text[:start_idx] + xaml_content + text[end_idx:]
    with open("SidebarDiagnostics/Settings.xaml", "w", encoding="utf-8") as f:
        f.write(new_text)
    print("XAML successfully updated for tabs 1,2,3")
else:
    print("Failed to find insertion points")

