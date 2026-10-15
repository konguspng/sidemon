import re

file_path = "SidebarDiagnostics/Settings.xaml"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

old_slider = """<Slider x:Name="UIScaleSlider" Value="{Binding Path=UIScale, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="0.5" Maximum="{Binding Path=MaxUIScale, Mode=OneWay}" TickFrequency="0.1" LargeChange="0.5" ToolTip="{x:Static frame:Resources.SettingsUIScaleTooltip}" />"""
new_slider = """<Slider x:Name="UIScaleSlider" Value="{Binding Path=UIScale, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Minimum="0.5" Maximum="3.0" TickFrequency="0.05" IsSnapToTickEnabled="True" LargeChange="0.5" ToolTip="{x:Static frame:Resources.SettingsUIScaleTooltip}" />"""

content = content.replace(old_slider, new_slider)

old_textblock = """
                    <TextBlock Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="1" TextWrapping="Wrap" Foreground="#B45309" FontSize="11" Margin="0,2,0,4">
                        <TextBlock.Style>
                            <Style TargetType="{x:Type TextBlock}">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding Path=UIScaleCapped, Mode=OneWay}" Value="True">
                                        <Setter Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                        <TextBlock.Text>
                            <Binding Path="MaxUIScale" Mode="OneWay" StringFormat="Your enabled monitors need more room than the screen at full size, so UI Scale is capped at {0:F2} to keep everything visible. Disable some monitors/drives to raise the cap." />
                        </TextBlock.Text>
                    </TextBlock>
"""
new_textblock = """
                    <StackPanel Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="1" Margin="0,2,0,4">
                        <CheckBox Content="{x:Static frame:Resources.SettingsAutoFitScale}" IsChecked="{Binding Path=AutoFitScale}" Margin="0,0,0,4" ToolTip="{x:Static frame:Resources.SettingsAutoFitScaleTooltip}" />
                        
                        <TextBlock TextWrapping="Wrap" FontSize="11">
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
                                <Binding Path="MaxUIScale" Mode="OneWay" StringFormat="Auto-fit reduced the scale to {0:F2} because your monitors/drives don't fit the screen height. Untick 'Shrink automatically' to set it yourself." />
                            </TextBlock.Text>
                        </TextBlock>
                    </StackPanel>
"""

content = content.replace(old_textblock.strip(), new_textblock.strip())

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
