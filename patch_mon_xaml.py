import re

file_path = "SidebarDiagnostics/Settings.xaml"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

start_str = '<TabItem Header="{x:Static frame:Resources.SettingsMonitorsTab}">'
end_str = '</TabItem>'

start_idx = content.find(start_str)
end_idx = content.find('<TabItem Header="{x:Static frame:Resources.SettingsHotkeysTab}">')
end_idx = content.rfind(end_str, start_idx, end_idx) + len(end_str)

new_xaml = """<TabItem Header="{x:Static frame:Resources.SettingsMonitorsTab}">
                <Grid Margin="0,0,10,10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="220" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    
                    <DockPanel LastChildFill="True" Grid.Column="0" Margin="0,0,10,0">
                        <StackPanel DockPanel.Dock="Top" Margin="0,0,0,10">
                            <TextBlock Text="Monitors" Style="{StaticResource SettingsSectionHeader}" />
                            <TextBlock Text="{x:Static frame:Resources.SettingsMonitorsSubtitle1}" Style="{StaticResource SettingsSectionHelper}" />
                            <TextBlock Text="{x:Static frame:Resources.SettingsMonitorsSubtitle2}" Style="{StaticResource SettingsSectionHelper}" />
                        </StackPanel>
                        
                        <ListBox x:Name="MonitorsList" ItemsSource="{Binding Path=MonitorConfig}" SelectedItem="{Binding Path=SelectedMonitorConfig, Mode=TwoWay}" ScrollViewer.HorizontalScrollBarVisibility="Disabled" BorderThickness="1" BorderBrush="#ccc" Background="White">
                            <ListBox.ItemContainerStyle>
                                <Style TargetType="ListBoxItem">
                                    <Setter Property="Padding" Value="5" />
                                    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                                </Style>
                            </ListBox.ItemContainerStyle>
                            <ListBox.ItemTemplate>
                                <DataTemplate>
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="Auto" />
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="Auto" />
                                        </Grid.ColumnDefinitions>
                                        <CheckBox Grid.Column="0" IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" VerticalAlignment="Center" Margin="0,0,5,0" ToolTip="{x:Static frame:Resources.SettingsMonitorEnabledTooltip}" />
                                        <TextBlock Grid.Column="1" Text="{Binding Path=Name}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                        <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="5,0,0,0">
                                            <Button Content="&#x25B2;" Width="20" Height="20" Margin="0,0,2,0" Click="MonitorUp_Click" ToolTip="Move Up" />
                                            <Button Content="&#x25BC;" Width="20" Height="20" Click="MonitorDown_Click" ToolTip="Move Down" />
                                        </StackPanel>
                                    </Grid>
                                </DataTemplate>
                            </ListBox.ItemTemplate>
                        </ListBox>
                    </DockPanel>
                    
                    <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" DataContext="{Binding Path=SelectedMonitorConfig}">
                        <StackPanel>
                            <!-- Hide details entirely if nothing is selected -->
                            <StackPanel.Style>
                                <Style TargetType="StackPanel">
                                    <Setter Property="Visibility" Value="Visible" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding}" Value="{x:Null}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </StackPanel.Style>
                            
                            <!-- No Extra Options text -->
                            <TextBlock Text="{x:Static frame:Resources.SettingsNoExtraOptions}" Style="{StaticResource SettingsSectionHelper}" Margin="0,5,0,0">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock" BasedOn="{StaticResource SettingsSectionHelper}">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                        <Style.Triggers>
                                            <MultiDataTrigger>
                                                <MultiDataTrigger.Conditions>
                                                    <Condition Binding="{Binding Path=HardwareOC.Count}" Value="0" />
                                                    <Condition Binding="{Binding Path=Metrics.Count}" Value="0" />
                                                    <Condition Binding="{Binding Path=Params.Count}" Value="0" />
                                                </MultiDataTrigger.Conditions>
                                                <Setter Property="Visibility" Value="Visible" />
                                            </MultiDataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                            
                            <!-- Providers Section (Only for AIUsage) -->
                            <Border Style="{StaticResource SettingsSectionBorder}">
                                <Border.Style>
                                    <Style TargetType="Border" BasedOn="{StaticResource SettingsSectionBorder}">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Path=Type}" Value="{x:Static monitor:MonitorType.AIUsage}">
                                                <Setter Property="Visibility" Value="Visible" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <StackPanel>
                                    <TextBlock Text="{x:Static frame:Resources.SettingsProvidersHeader}" Style="{StaticResource SettingsSectionHeader}" />
                                    <TextBlock Text="{x:Static frame:Resources.SettingsUsageNote}" Style="{StaticResource SettingsSectionHelper}" TextWrapping="Wrap" Margin="0,0,0,8" />
                                    
                                    <Grid Margin="0,0,0,5">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="25" />
                                            <ColumnDefinition SharedSizeGroup="NameCol" Width="Auto" />
                                            <ColumnDefinition Width="155" />
                                            <ColumnDefinition Width="65" />
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="45" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="1" Text="{x:Static frame:Resources.SettingsUsageProviderColumn}" FontWeight="Bold" Margin="0,0,5,0" />
                                        <TextBlock Grid.Column="2" Text="{x:Static frame:Resources.SettingsUsageCustomPathColumn}" FontWeight="Bold" Margin="0,0,5,0" />
                                        <TextBlock Grid.Column="3" Text="{x:Static frame:Resources.SettingsUsageShowExtraColumn}" FontWeight="Bold" Margin="0,0,5,0" TextWrapping="Wrap" />
                                        <TextBlock Grid.Column="4" Text="{x:Static frame:Resources.SettingsUsageStatusColumn}" FontWeight="Bold" Margin="0,0,5,0" />
                                    </Grid>
                                    
                                    <ItemsControl ItemsSource="{Binding Path=HardwareOC}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <Grid Margin="0,5">
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="25" />
                                                        <ColumnDefinition SharedSizeGroup="NameCol" Width="Auto" />
                                                        <ColumnDefinition Width="155" />
                                                        <ColumnDefinition Width="65" />
                                                        <ColumnDefinition Width="*" />
                                                        <ColumnDefinition Width="45" />
                                                    </Grid.ColumnDefinitions>
                                                    
                                                    <CheckBox Grid.Column="0" IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsUsageEnabledTooltip}" VerticalAlignment="Center" Margin="0,0,5,0" />
                                                    
                                                    <TextBox Grid.Column="1" Text="{Binding Path=Name, Mode=TwoWay}" Width="100" Margin="0,0,5,0" ToolTip="{x:Static frame:Resources.SettingsHardwareNameTooltip}" />
                                                    
                                                    <TextBox Grid.Column="2" Text="{Binding Path=CustomPath, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" Margin="0,0,5,0" ToolTip="{x:Static frame:Resources.SettingsUsageCustomPathTooltip}" />
                                                    
                                                    <CheckBox Grid.Column="3" IsChecked="{Binding Path=ShowExtra, TargetNullValue=True, FallbackValue=True, Mode=TwoWay}" Margin="0,0,5,0" VerticalAlignment="Center" HorizontalAlignment="Center">
                                                        <CheckBox.Style>
                                                            <Style TargetType="CheckBox">
                                                                <Setter Property="Visibility" Value="Hidden" />
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding ID}" Value="AIUsage_Agy">
                                                                        <Setter Property="Visibility" Value="Visible" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </CheckBox.Style>
                                                    </CheckBox>
                                                    
                                                    <TextBlock Grid.Column="4" Text="{Binding Path=Status, Mode=OneWay}" VerticalAlignment="Center" Margin="0,0,5,0" TextTrimming="CharacterEllipsis" />
                                                    
                                                    <StackPanel Grid.Column="5" Orientation="Horizontal" VerticalAlignment="Center">
                                                        <Button Content="&#x25B2;" Width="20" Height="20" Margin="0,0,2,0" Click="HardwareUp_Click" ToolTip="Move Up" />
                                                        <Button Content="&#x25BC;" Width="20" Height="20" Click="HardwareDown_Click" ToolTip="Move Down" />
                                                    </StackPanel>
                                                </Grid>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </StackPanel>
                            </Border>
                            
                            <!-- Hardware Section (Not for AIUsage) -->
                            <Border Style="{StaticResource SettingsSectionBorder}">
                                <Border.Style>
                                    <Style TargetType="Border" BasedOn="{StaticResource SettingsSectionBorder}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Path=HardwareOC.Count}" Value="0">
                                                <Setter Property="Visibility" Value="Collapsed" />
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Path=Type}" Value="{x:Static monitor:MonitorType.AIUsage}">
                                                <Setter Property="Visibility" Value="Collapsed" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <StackPanel>
                                    <TextBlock Text="{x:Static frame:Resources.SettingsHardwareHeader}" Style="{StaticResource SettingsSectionHeader}" />
                                    <ItemsControl ItemsSource="{Binding Path=HardwareOC}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <Grid Margin="0,5">
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="Auto" />
                                                        <ColumnDefinition Width="*" />
                                                        <ColumnDefinition Width="Auto" />
                                                    </Grid.ColumnDefinitions>
                                                    
                                                    <CheckBox Grid.Column="0" IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{x:Static frame:Resources.SettingsHardwareEnabledTooltip}" VerticalAlignment="Center" Margin="0,0,5,0" />
                                                    
                                                    <TextBox Grid.Column="1" Text="{Binding Path=Name, Mode=TwoWay}" Margin="0,0,5,0" ToolTip="{x:Static frame:Resources.SettingsHardwareNameTooltip}" />
                                                    
                                                    <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                                                        <Button Content="&#x25B2;" Width="20" Height="20" Margin="0,0,2,0" Click="HardwareUp_Click" ToolTip="Move Up" />
                                                        <Button Content="&#x25BC;" Width="20" Height="20" Click="HardwareDown_Click" ToolTip="Move Down" />
                                                    </StackPanel>
                                                </Grid>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </StackPanel>
                            </Border>
                            
                            <!-- Metrics Section -->
                            <Border Style="{StaticResource SettingsSectionBorder}">
                                <Border.Style>
                                    <Style TargetType="Border" BasedOn="{StaticResource SettingsSectionBorder}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Path=Metrics.Count}" Value="0">
                                                <Setter Property="Visibility" Value="Collapsed" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <StackPanel>
                                    <TextBlock Text="{x:Static frame:Resources.SettingsMetricsHeader}" Style="{StaticResource SettingsSectionHeader}" />
                                    <ItemsControl ItemsSource="{Binding Path=Metrics, Mode=OneWay}" ToolTip="{x:Static frame:Resources.SettingsMetricsTooltip}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <CheckBox Content="{Binding Path=Name, Mode=OneWay}" IsChecked="{Binding Path=Enabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Margin="0,5,10,5" />
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                        <ItemsControl.ItemsPanel>
                                            <ItemsPanelTemplate>
                                                <WrapPanel Orientation="Horizontal" />
                                            </ItemsPanelTemplate>
                                        </ItemsControl.ItemsPanel>
                                    </ItemsControl>
                                </StackPanel>
                            </Border>
                            
                            <!-- Parameters Section -->
                            <Border Style="{StaticResource SettingsSectionBorder}">
                                <Border.Style>
                                    <Style TargetType="Border" BasedOn="{StaticResource SettingsSectionBorder}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Path=Params.Count}" Value="0">
                                                <Setter Property="Visibility" Value="Collapsed" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <StackPanel>
                                    <TextBlock Text="{x:Static frame:Resources.SettingsAlertsHeader}" Style="{StaticResource SettingsSectionHeader}" />
                                    <ItemsControl ItemsSource="{Binding Path=Params}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate DataType="{x:Type monitor:ConfigParam}">
                                                <ContentControl>
                                                    <ContentControl.Style>
                                                        <Style TargetType="{x:Type ContentControl}">
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding TypeString}" Value="System.Boolean">
                                                                    <Setter Property="Content">
                                                                        <Setter.Value>
                                                                            <CheckBox Content="{Binding Path=Name}" IsChecked="{Binding Path=Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" ToolTip="{Binding Path=Tooltip, Mode=OneTime}" Margin="0,5" />
                                                                        </Setter.Value>
                                                                    </Setter>
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding TypeString}" Value="System.Int32">
                                                                    <Setter Property="Content">
                                                                        <Setter.Value>
                                                                            <StackPanel Orientation="Horizontal" Margin="0,5">
                                                                                <TextBlock Text="{Binding Path=Name, Mode=OneTime}" VerticalAlignment="Center" Margin="0,0,10,0" />
                                                                                <TextBox Text="{Binding Path=Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, Converter={StaticResource IntConverter}}" ToolTip="{Binding Path=Tooltip, Mode=OneTime}" Width="50" VerticalAlignment="Center" />
                                                                            </StackPanel>
                                                                        </Setter.Value>
                                                                    </Setter>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </ContentControl.Style>
                                                </ContentControl>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </StackPanel>
                            </Border>
                        </StackPanel>
                    </ScrollViewer>
                </Grid>
            </TabItem>"""

new_content = content[:start_idx] + new_xaml + content[end_idx:]

with open(file_path, "w", encoding="utf-8") as f:
    f.write(new_content)
